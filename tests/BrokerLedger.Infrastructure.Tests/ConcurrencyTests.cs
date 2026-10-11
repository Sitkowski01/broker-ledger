using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Wiele zadan naraz na tych samych kontach — to, czego nie sprawdzi ksiega w pamieci.</summary>
[Collection(SharedPostgres.Name)]
public sealed class ConcurrencyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Parallel = 20;
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private readonly ClientId _client = ClientId.New();
    private TestDatabase _db = null!;
    private PostgresLedgerStore _store = null!;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        _store = _db.Store();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Parallel_withdrawals_never_take_more_than_the_balance()
    {
        await _store.PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(100m)));

        // 20 wyplat po 10 zl z 100 zl: przejsc moze dokladnie 10, reszta musi dostac brak srodkow.
        var results = await RunAllAsync(i => _store.PostAsync($"wd-{i}", JournalEntries.Withdrawal(_client, Money.Pln(10m))));

        Assert.Equal(10, results.Count(r => r is PostOutcome.Posted));
        Assert.Equal(10, results.Count(r => r is InsufficientBalanceException));
        Assert.Equal(0m, await _store.BalanceAsync(AccountId.Available(_client, Pln)));
        await AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Parallel_repeats_of_one_key_book_it_once()
    {
        var results = await RunAllAsync(_ => _store.PostAsync("transfer-1", JournalEntries.Deposit(_client, Money.Pln(100m))));

        Assert.Equal(1, results.Count(r => r is PostOutcome.Posted));
        Assert.Equal(Parallel - 1, results.Count(r => r is PostOutcome.AlreadyPosted));
        Assert.Equal(100m, await _store.BalanceAsync(AccountId.Available(_client, Pln)));
        Assert.Equal(1, await _db.CountAsync("journal_entries"));
    }

    [Fact]
    public async Task Opposite_moves_on_the_same_accounts_do_not_deadlock()
    {
        // Blokada idzie z wolnych do zablokowanych, zwolnienie odwrotnie — bez stalej kolejnosci
        // blokowania wierszy taka para potrafi sie zakleszczyc (PostgreSQL 40P01).
        await _store.PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(1000m)));
        await _store.PostAsync("block-0", JournalEntries.Block(_client, Pln, 500m));

        var results = await RunAllAsync(i => _store.PostAsync($"move-{i}", i % 2 == 0
            ? JournalEntries.Block(_client, Pln, 1m)
            : JournalEntries.Release(_client, Pln, 1m)));

        Assert.All(results, r => Assert.Equal(PostOutcome.Posted, r));
        Assert.Equal(500m, await _store.BalanceAsync(AccountId.Blocked(_client, Pln)));
        await AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Many_clients_at_once_keep_the_house_accounts_right()
    {
        var clients = Enumerable.Range(0, Parallel).Select(_ => ClientId.New()).ToArray();

        await RunAllAsync(i => _store.PostAsync($"dep-{i}", JournalEntries.Deposit(clients[i], Money.Pln(10.01m))));

        Assert.Equal(Parallel * 10.01m, await _store.BalanceAsync(AccountId.Bank(Currency.Pln)));
        await AssertLedgerConsistentAsync();
    }

    /// <summary>Startuje wszystkie zadania naraz i zbiera wynik albo wyjatek kazdego.</summary>
    private static async Task<object[]> RunAllAsync(Func<int, Task<PostOutcome>> post)
    {
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, Parallel).Select(i => Task.Run(async () =>
        {
            start.Wait();
            try
            {
                return (object)await post(i);
            }
            catch (InsufficientBalanceException e)
            {
                return e;
            }
        })).ToArray();
        start.Set();
        return await Task.WhenAll(tasks);
    }

    private async Task AssertLedgerConsistentAsync()
    {
        Assert.Empty(await _db.Audit.BalanceMismatchesAsync());
        Assert.Empty(await _db.Audit.TrialBalanceGapsAsync());
    }
}
