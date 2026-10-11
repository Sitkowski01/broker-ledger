using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Przypadki, ktore wyszly w przegladzie kodu: puste konta, kolejnosc historii, migracja z danymi.</summary>
[Collection(SharedPostgres.Name)]
public sealed class ReviewFixTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly ClientId _client = ClientId.New();
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await postgres.CreateDatabaseAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Rejected_entry_does_not_leave_empty_accounts_behind()
    {
        await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            _db.Store().PostAsync("wd", JournalEntries.Withdrawal(_client, Money.Of(1m, new Currency("USD")))));

        Assert.Equal(0, await _db.CountAsync("accounts"));
        Assert.Empty(await _db.Queries().ClientBalancesAsync(_client));
    }

    [Fact]
    public async Task History_keeps_booking_order_within_the_same_millisecond()
    {
        // Zegar stoi, wiec wszystkie dokumenty maja ten sam czas — a UUID v7 w tej samej milisekundzie
        // ma losowy ogon. Kolejnosc musi i tak byc kolejnoscia zaksiegowania.
        var store = _db.Store(new FixedClock(DateTimeOffset.UnixEpoch));
        foreach (var i in Enumerable.Range(1, 30))
        {
            await store.PostAsync($"dep-{i}", JournalEntries.Deposit(_client, Money.Pln(i)));
        }

        var history = await _db.Queries().ClientHistoryAsync(_client, limit: 30);

        Assert.Equal(Enumerable.Range(1, 30).Reverse().Select(i => (decimal)i), history.Select(h => h.Postings[0].Amount));
        Assert.Equal(history.Select(h => h.Sequence).OrderDescending(), history.Select(h => h.Sequence));
    }

    [Fact]
    public async Task New_accounts_created_in_opposite_order_do_not_deadlock()
    {
        // Blokada zaklada "wolne" i "zablokowane" w tej kolejnosci, zwolnienie odwrotnie. Bez stalej
        // kolejnosci zakladania dwie transakcje czekalyby na swoje niezatwierdzone konta (40P01).
        var store = _db.Store();
        var clients = Enumerable.Range(0, 10).Select(_ => ClientId.New()).ToArray();
        var cdr = Asset.Shares("CDR");

        var attempts = clients.SelectMany(c => new[]
        {
            Task.Run(() => store.PostAsync($"block-{c}", JournalEntries.Block(c, cdr, 1m))),
            Task.Run(() => store.PostAsync($"release-{c}", JournalEntries.Release(c, cdr, 1m))),
        });
        var results = await Task.WhenAll(attempts.Select(async t =>
        {
            try
            {
                await t;
                return "posted";
            }
            catch (InsufficientBalanceException)
            {
                return "insufficient";
            }
        }));

        Assert.All(results, r => Assert.Equal("insufficient", r));
    }

    [Fact]
    public async Task Balances_are_sorted_by_asset_then_account()
    {
        var store = _db.Store();
        await store.PostAsync("pko", JournalEntries.SharesTransferIn(_client, "PKO", 1));
        await store.PostAsync("cdr", JournalEntries.SharesTransferIn(_client, "CDR", 2));
        await store.PostAsync("block", JournalEntries.Block(_client, Asset.Shares("CDR"), 1m));

        var balances = await _db.Queries().ClientBalancesAsync(_client);

        Assert.Equal(
            [AccountId.Available(_client, Asset.Shares("CDR")), AccountId.Blocked(_client, Asset.Shares("CDR")),
             AccountId.Available(_client, Asset.Shares("PKO"))],
            balances.Select(b => b.Account));
    }

    [Fact]
    public async Task History_keeps_the_order_of_postings_within_an_entry()
    {
        await _db.Store().PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(10m)));
        await _db.Store().PostAsync("block", JournalEntries.Block(_client, Asset.Cash(Currency.Pln), 4m));

        var block = (await _db.Queries().ClientHistoryAsync(_client, limit: 1))[0];

        Assert.Equal(JournalEntries.Block(_client, Asset.Cash(Currency.Pln), 4m).Postings, block.Postings);
    }

    [Fact]
    public async Task Password_hash_can_be_upgraded()
    {
        var clients = _db.Clients();
        await clients.TryAddAsync(new ClientAccount(_client, new EmailAddress("jan@example.com"), "old-hash"));

        await clients.UpdatePasswordHashAsync(_client, "new-hash");

        Assert.Equal("new-hash", (await clients.FindByEmailAsync(new EmailAddress("jan@example.com")))!.PasswordHash);
        await Assert.ThrowsAsync<ArgumentNullException>(() => clients.UpdatePasswordHashAsync(null!, "x"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => clients.UpdatePasswordHashAsync(_client, " "));
    }

    [Fact]
    public async Task Upgrading_a_database_with_entries_numbers_the_old_ones()
    {
        // Baza zalozona pierwsza migracja i z danymi, potem aktualizacja do biezacego schematu.
        var fresh = await postgres.CreateEmptyDatabaseAsync();
        await using (var db = fresh.Context())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261011160722_InitialLedger");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO journal_entries (id, idempotency_key, description, created_at)
                VALUES ('00000000-0000-0000-0000-000000000002', 'b', 'second', now()),
                       ('00000000-0000-0000-0000-000000000001', 'a', 'first', now());
                """);
            await db.Database.MigrateAsync();
        }

        await using var context = fresh.Context();
        var sequences = await context.JournalEntries.OrderBy(e => e.Sequence).Select(e => e.Sequence).ToListAsync();
        Assert.Equal([1L, 2L], sequences);
        await fresh.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
