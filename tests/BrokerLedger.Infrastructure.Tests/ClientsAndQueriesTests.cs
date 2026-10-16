using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

[Collection(SharedPostgres.Name)]
public sealed class ClientsAndQueriesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private readonly ClientId _client = ClientId.New();
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await postgres.CreateDatabaseAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Client_is_stored_and_found_by_email()
    {
        var clients = _db.Clients();
        var jan = new ClientAccount(_client, new EmailAddress("jan@example.com"), "hash-1");

        Assert.True(await clients.TryAddAsync(jan));

        Assert.Equal(jan, await clients.FindByEmailAsync(new EmailAddress("JAN@example.com")));
        Assert.Null(await clients.FindByEmailAsync(new EmailAddress("ola@example.com")));
    }

    [Fact]
    public async Task Taken_email_is_refused_even_under_a_race()
    {
        var clients = _db.Clients();
        var email = new EmailAddress("jan@example.com");

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => clients.TryAddAsync(new ClientAccount(ClientId.New(), email, "hash"))));

        Assert.Single(results, added => added);
        Assert.Equal(1, await _db.CountAsync("clients"));
    }

    [Fact]
    public async Task Balances_list_only_the_clients_own_accounts()
    {
        var store = _db.Store();
        await store.PostAsync("a", JournalEntries.Deposit(_client, Money.Pln(100m)));
        await store.PostAsync("b", JournalEntries.Block(_client, Pln, 30m));
        await store.PostAsync("c", JournalEntries.SharesTransferIn(_client, "CDR", 5));
        await store.PostAsync("d", JournalEntries.Deposit(ClientId.New(), Money.Pln(999m)));

        var balances = await _db.Queries().ClientBalancesAsync(_client);

        Assert.Equal(
            [(AccountId.Available(_client, Pln), 70m), (AccountId.Blocked(_client, Pln), 30m),
             (AccountId.Available(_client, Asset.Shares("CDR")), 5m)],
            balances.Select(b => (b.Account, b.Balance)));
    }

    [Fact]
    public async Task History_shows_the_clients_and_brokers_postings_newest_first()
    {
        var store = _db.Store();
        var at = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var order = OrderId.New();
        await _db.Store(new FixedClock(at)).PostAsync("a", JournalEntries.Deposit(_client, Money.Pln(100m)));
        await store.PostAsync("b", JournalEntries.Deposit(ClientId.New(), Money.Pln(5m)));
        await store.PostAsync($"order:{order}:block", JournalEntries.Block(_client, Pln, 40m));

        var history = await _db.Queries().ClientHistoryAsync(_client, limit: 10);

        Assert.Equal(["block", "deposit"], history.Select(h => h.Description));
        // Wplata ma dwa zapisy: bank brokera (Wn) i konto klienta (Ma) — klient widzi obie strony.
        Assert.Equal(
            [Posting.Debit(AccountId.Bank(Currency.Pln), 100m), Posting.Credit(AccountId.Available(_client, Pln), 100m)],
            history[1].Postings);
        Assert.Equal((at, null), (history[1].CreatedAt, history[1].Order));
        Assert.Equal((2, order), (history[0].Postings.Count, history[0].Order));
    }

    [Fact]
    public async Task History_filters_by_description_and_carries_the_key()
    {
        var store = _db.Store();
        await store.PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(100m)));
        await store.PostAsync("blk", JournalEntries.Block(_client, Pln, 40m));
        await store.PostAsync("wd", JournalEntries.Withdrawal(_client, Money.Pln(10m)));

        var transfers = await _db.Queries().ClientHistoryAsync(_client, limit: 10, descriptions: ["deposit", "withdrawal"]);
        var page = await _db.Queries().ClientHistoryAsync(_client, limit: 1, before: transfers[0].Sequence, descriptions: ["deposit", "withdrawal"]);

        Assert.Equal([("withdrawal", "wd"), ("deposit", "dep")], transfers.Select(h => (h.Description, h.Key)));
        // Kursor dziala tez z filtrem: blokada miedzy nimi nie zajmuje miejsca na stronie.
        Assert.Equal("dep", Assert.Single(page).Key);
    }

    [Fact]
    public async Task History_never_shows_another_clients_account()
    {
        var other = ClientId.New();
        var store = _db.Store();
        await store.PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(100m)));
        await store.PostAsync("gift", new JournalEntry("transfer",
            Posting.Debit(AccountId.Available(_client, Pln), 30m), Posting.Credit(AccountId.Available(other, Pln), 30m)));

        var transfer = (await _db.Queries().ClientHistoryAsync(_client, limit: 10))[0];

        Assert.Equal([Posting.Debit(AccountId.Available(_client, Pln), 30m)], transfer.Postings);
    }

    [Fact]
    public async Task Portfolio_entries_keep_only_the_clients_postings()
    {
        await _db.Store().PostAsync("in", JournalEntries.SharesTransferIn(_client, "CDR", 5));

        var entry = Assert.Single(await _db.Queries().ClientShareEntriesAsync(_client));

        Assert.Equal([Posting.Credit(AccountId.Available(_client, Asset.Shares("CDR")), 5m)], entry.Postings);
    }

    [Fact]
    public async Task History_pages_with_the_last_id_as_cursor()
    {
        var store = _db.Store();
        foreach (var i in Enumerable.Range(1, 5))
        {
            await store.PostAsync($"dep-{i}", JournalEntries.Deposit(_client, Money.Pln(i)));
        }
        var queries = _db.Queries();

        var first = await queries.ClientHistoryAsync(_client, limit: 2);
        var second = await queries.ClientHistoryAsync(_client, limit: 2, before: first[^1].Sequence);
        var last = await queries.ClientHistoryAsync(_client, limit: 2, before: second[^1].Sequence);

        Assert.Equal([5m, 4m, 3m, 2m, 1m], first.Concat(second).Concat(last).Select(h => h.Postings[0].Amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task History_page_size_is_bounded(int limit) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _db.Queries().ClientHistoryAsync(_client, limit));

    [Fact]
    public async Task Rejects_missing_arguments()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _db.Clients().TryAddAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _db.Clients().FindByEmailAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _db.Queries().ClientBalancesAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _db.Queries().ClientHistoryAsync(null!, 1));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
