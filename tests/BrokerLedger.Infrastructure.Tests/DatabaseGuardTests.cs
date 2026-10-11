using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>
/// Ochrona po stronie bazy — na wypadek bledu w kodzie albo recznego SQL-a na produkcji.
/// I kontrola, ktora takie reczne zmiany wykrywa.
/// </summary>
[Collection(SharedPostgres.Name)]
public sealed class DatabaseGuardTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly ClientId _client = ClientId.New();
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        await _db.Store().PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(100m)));
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Theory]
    [InlineData("UPDATE postings SET amount = 1", "UPDATE on postings")]
    [InlineData("DELETE FROM postings", "DELETE on postings")]
    [InlineData("TRUNCATE postings CASCADE", "TRUNCATE on postings")]
    [InlineData("UPDATE journal_entries SET description = 'x'", "UPDATE on journal_entries")]
    [InlineData("DELETE FROM journal_entries", "DELETE on journal_entries")]
    public async Task Ledger_history_cannot_be_rewritten(string sql, string expected)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => _db.ExecuteAsync(sql));

        Assert.Contains($"ledger is append-only: {expected}", ex.MessageText, StringComparison.Ordinal);
        Assert.Equal(2, await _db.CountAsync("postings"));
    }

    [Fact]
    public async Task Balance_cannot_be_negative_even_by_hand()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => _db.ExecuteAsync("UPDATE accounts SET balance = -0.01"));

        Assert.Equal("ck_accounts_balance_not_negative", ex.ConstraintName);
    }

    [Fact]
    public async Task Posting_amount_must_be_positive()
    {
        await using var db = _db.Context();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO postings (entry_id, account_id, side, amount) SELECT entry_id, account_id, side, 0 FROM postings LIMIT 1"));

        Assert.Equal("ck_postings_amount_positive", ex.ConstraintName);
    }

    [Fact]
    public async Task Audit_finds_a_balance_changed_behind_the_ledgers_back()
    {
        Assert.Empty(await _db.Audit.BalanceMismatchesAsync());

        await _db.ExecuteAsync("UPDATE accounts SET balance = 150 WHERE type = 'ClientAvailable'");

        var mismatch = Assert.Single(await _db.Audit.BalanceMismatchesAsync());
        Assert.Equal((150m, 100m), (mismatch.Balance, mismatch.FromPostings));
        var gap = Assert.Single(await _db.Audit.TrialBalanceGapsAsync());
        Assert.Equal(("Cash", "PLN", 100m, 150m), (gap.AssetKind, gap.AssetCode, gap.DebitSide, gap.CreditSide));
    }

    [Fact]
    public async Task Audit_counts_an_account_without_postings_as_zero()
    {
        await _db.ExecuteAsync("""
            INSERT INTO accounts (type, asset_kind, asset_code, client_id, normal_side, balance)
            VALUES ('Depository', 'Shares', 'CDR', NULL, 'Debit', 5)
            """);

        var mismatch = Assert.Single(await _db.Audit.BalanceMismatchesAsync());
        Assert.Equal((5m, 0m), (mismatch.Balance, mismatch.FromPostings));
        var gap = Assert.Single(await _db.Audit.TrialBalanceGapsAsync());
        Assert.Equal(("CDR", 5m, 0m), (gap.AssetCode, gap.DebitSide, gap.CreditSide));
    }

    [Fact]
    public async Task House_account_exists_once_even_without_a_client()
    {
        // NULL w client_id normalnie nie liczy sie jako duplikat — indeks ma NULLS NOT DISTINCT.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => _db.ExecuteAsync("""
            INSERT INTO accounts (type, asset_kind, asset_code, client_id, normal_side, balance)
            VALUES ('Bank', 'Cash', 'PLN', NULL, 'Debit', 0)
            """));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
    }

    [Fact]
    public async Task Client_account_without_a_client_is_reported_not_guessed()
    {
        // Uszkodzony wiersz: konto klienta bez klienta. Odczyt dokumentu ma sie wywalic z nazwa konta,
        // a nie po cichu przypisac zapis komus innemu.
        await _db.ExecuteAsync("""
            INSERT INTO accounts (type, asset_kind, asset_code, client_id, normal_side, balance)
            VALUES ('ClientBlocked', 'Cash', 'PLN', NULL, 'Credit', 0);
            INSERT INTO journal_entries (id, idempotency_key, description, created_at)
            VALUES (gen_random_uuid(), 'broken', 'deposit', now());
            INSERT INTO postings (entry_id, account_id, side, amount)
            SELECT (SELECT id FROM journal_entries WHERE idempotency_key = 'broken'), id, 'Credit', 1
            FROM accounts WHERE type = 'ClientBlocked';
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _db.Store().PostAsync("broken", JournalEntries.Deposit(_client, Money.Pln(1m))));

        Assert.Matches(@"^client account \d+ has no client$", ex.Message);
    }
}
