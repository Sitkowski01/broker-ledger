using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>
/// Test roznicowy: ten sam losowy ciag operacji idzie do ksiegi w pamieci (wzorzec z domeny)
/// i do PostgreSQL. Obie musza przyjac te same dokumenty, odrzucic pozostale z tym samym kontem i saldem w wyjatku
/// i skonczyc z tymi samymi saldami.
/// </summary>
[Collection(SharedPostgres.Name)]
public sealed class DifferentialTests(PostgresFixture postgres)
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private static readonly Asset Cdr = Asset.Shares("CDR");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Postgres_agrees_with_the_in_memory_ledger(int seed)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var store = db.Store();
        var model = new Ledger();
        var random = new Random(seed);
        var clients = Enumerable.Range(0, 3).Select(_ => ClientId.New()).ToArray();

        for (var step = 0; step < 150; step++)
        {
            var entry = RandomEntry(random, clients[random.Next(clients.Length)]);
            var modelShortfall = TryPost(model, entry);

            if (modelShortfall is null)
            {
                Assert.Equal(PostOutcome.Posted, await store.PostAsync($"op-{step}", entry));
            }
            else
            {
                var shortfall = await Assert.ThrowsAsync<InsufficientBalanceException>(() => store.PostAsync($"op-{step}", entry));
                Assert.Equal((modelShortfall.Account, modelShortfall.Balance), (shortfall.Account, shortfall.Balance));
            }
        }

        var accounts = model.Entries.SelectMany(e => e.Postings).Select(p => p.Account).Distinct();
        foreach (var account in accounts)
        {
            Assert.Equal(model.Balance(account), await store.BalanceAsync(account));
        }
        Assert.Equal(model.Entries.Count, await db.CountAsync("journal_entries"));
        Assert.Empty(await db.Audit.BalanceMismatchesAsync());
        Assert.Empty(await db.Audit.TrialBalanceGapsAsync());
    }

    private static JournalEntry RandomEntry(Random random, ClientId client)
    {
        var shares = random.Next(2) == 0;
        var asset = shares ? Cdr : Pln;
        // Male kwoty, zeby czesto brakowalo srodkow.
        var units = random.Next(1, 300);
        var amount = shares ? units : units / 100m;
        return random.Next(4) switch
        {
            0 when shares => JournalEntries.SharesTransferIn(client, "CDR", units),
            0 => JournalEntries.Deposit(client, Money.Pln(amount)),
            1 => JournalEntries.Withdrawal(client, Money.Pln(units / 100m)),
            2 => JournalEntries.Block(client, asset, amount),
            _ => JournalEntries.Release(client, asset, amount),
        };
    }

    private static InsufficientBalanceException? TryPost(Ledger ledger, JournalEntry entry)
    {
        try
        {
            ledger.Post(entry);
            return null;
        }
        catch (InsufficientBalanceException e)
        {
            return e;
        }
    }
}
