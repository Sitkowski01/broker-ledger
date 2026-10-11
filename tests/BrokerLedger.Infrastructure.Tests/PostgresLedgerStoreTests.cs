using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

[Collection(SharedPostgres.Name)]
public sealed class PostgresLedgerStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private static readonly Asset Cdr = Asset.Shares("CDR");
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
    public async Task Stores_the_entry_and_moves_both_balances()
    {
        var outcome = await _store.PostAsync("dep-1", JournalEntries.Deposit(_client, Money.Pln(1000m)));

        Assert.Equal(PostOutcome.Posted, outcome);
        Assert.Equal(1000m, await _store.BalanceAsync(AccountId.Available(_client, Pln)));
        Assert.Equal(1000m, await _store.BalanceAsync(AccountId.Bank(Currency.Pln)));
        Assert.Equal(1, await _db.CountAsync("journal_entries"));
        Assert.Equal(2, await _db.CountAsync("postings"));
    }

    [Fact]
    public async Task Unknown_account_has_zero_balance() =>
        Assert.Equal(0m, await _store.BalanceAsync(AccountId.Blocked(_client, Cdr)));

    [Fact]
    public async Task Rejected_entry_rolls_back_completely()
    {
        await _store.PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(100m)));

        var ex = await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            _store.PostAsync("wd", JournalEntries.Withdrawal(_client, Money.Pln(100.01m))));

        Assert.Equal((AccountId.Available(_client, Pln), 100m, 100.01m), (ex.Account, ex.Balance, ex.Requested));
        Assert.Equal(100m, await _store.BalanceAsync(AccountId.Available(_client, Pln)));
        Assert.Equal(100m, await _store.BalanceAsync(AccountId.Bank(Currency.Pln)));
        Assert.Equal(1, await _db.CountAsync("journal_entries"));
        // Klucz odrzuconego dokumentu jest wolny — poprawione zlecenie moze go uzyc.
        Assert.Equal(PostOutcome.Posted, await _store.PostAsync("wd", JournalEntries.Withdrawal(_client, Money.Pln(100m))));
    }

    [Fact]
    public async Task Same_key_and_same_entry_is_booked_once()
    {
        var deposit = JournalEntries.Deposit(_client, Money.Pln(250.40m));

        Assert.Equal(PostOutcome.Posted, await _store.PostAsync("transfer-77", deposit));
        Assert.Equal(PostOutcome.AlreadyPosted, await _store.PostAsync("transfer-77", JournalEntries.Deposit(_client, Money.Pln(250.40m))));

        Assert.Equal(250.40m, await _store.BalanceAsync(AccountId.Available(_client, Pln)));
        Assert.Equal(1, await _db.CountAsync("journal_entries"));
    }

    [Fact]
    public async Task Same_key_with_a_different_entry_is_a_conflict()
    {
        await _store.PostAsync("transfer-77", JournalEntries.Deposit(_client, Money.Pln(100m)));

        var ex = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            _store.PostAsync("transfer-77", JournalEntries.Deposit(_client, Money.Pln(100.01m))));

        Assert.Equal("transfer-77", ex.IdempotencyKey);
        Assert.Equal(100m, await _store.BalanceAsync(AccountId.Available(_client, Pln)));
    }

    [Fact]
    public async Task Retried_withdrawal_that_emptied_the_account_is_a_repeat_not_a_shortfall()
    {
        await _store.PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(100m)));
        await _store.PostAsync("wd", JournalEntries.Withdrawal(_client, Money.Pln(100m)));

        Assert.Equal(PostOutcome.AlreadyPosted, await _store.PostAsync("wd", JournalEntries.Withdrawal(_client, Money.Pln(100m))));
    }

    [Fact]
    public async Task Repeat_is_recognised_for_every_account_type()
    {
        // Odczyt dokumentu z bazy musi odtworzyc kazde konto domeny — inaczej powtorka wygladalaby na konflikt.
        await _store.PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(5000m)));
        await _store.PostAsync("block", JournalEntries.Block(_client, Pln, 2510m));
        var buy = new JournalEntry("buy",
            Posting.Debit(AccountId.Blocked(_client, Pln), 2510m),
            Posting.Credit(AccountId.Bank(Currency.Pln), 2500m),
            Posting.Credit(AccountId.FeeIncome(Currency.Pln), 10m),
            Posting.Debit(AccountId.Depository("CDR"), 10m),
            Posting.Credit(AccountId.Available(_client, Cdr), 10m));

        Assert.Equal(PostOutcome.Posted, await _store.PostAsync("trade-1", buy));
        Assert.Equal(PostOutcome.AlreadyPosted, await _store.PostAsync("trade-1", buy));

        Assert.Equal(10m, await _store.BalanceAsync(AccountId.Available(_client, Cdr)));
        Assert.Equal(10m, await _store.BalanceAsync(AccountId.FeeIncome(Currency.Pln)));
        Assert.Equal(2500m, await _store.BalanceAsync(AccountId.Bank(Currency.Pln)));
        Assert.Empty(await _db.Audit.BalanceMismatchesAsync());
        Assert.Empty(await _db.Audit.TrialBalanceGapsAsync());
    }

    [Fact]
    public async Task Records_when_the_entry_was_booked()
    {
        var at = new DateTimeOffset(2026, 10, 11, 14, 3, 11, TimeSpan.Zero);

        await _db.Store(new FixedClock(at)).PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(1m)));

        await using var context = _db.Context();
        Assert.Equal(at, context.JournalEntries.Single().CreatedAt);
    }

    [Fact]
    public async Task Idempotency_key_has_a_length_limit()
    {
        var deposit = JournalEntries.Deposit(_client, Money.Pln(1m));

        Assert.Equal(PostOutcome.Posted, await _store.PostAsync(new string('k', ILedgerStore.MaxIdempotencyKeyLength), deposit));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _store.PostAsync(new string('k', 201), deposit));
        Assert.Equal("idempotencyKey", ex.ParamName);
        Assert.Contains("at most 200 characters", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_missing_arguments()
    {
        var deposit = JournalEntries.Deposit(_client, Money.Pln(1m));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.PostAsync(" ", deposit));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.PostAsync("k", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.BalanceAsync(null!));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
