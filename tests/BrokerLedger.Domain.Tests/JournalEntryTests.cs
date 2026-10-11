using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class JournalEntryTests
{
    private static readonly ClientId Client = ClientId.New();
    private static readonly AccountId Bank = AccountId.Bank(Currency.Pln);
    private static readonly AccountId Cash = AccountId.Available(Client, Asset.Cash(Currency.Pln));
    private static readonly AccountId Fees = AccountId.FeeIncome(Currency.Pln);
    private static readonly AccountId Depository = AccountId.Depository("CDR");
    private static readonly AccountId Shares = AccountId.Available(Client, Asset.Shares("CDR"));

    [Fact]
    public void Accepts_entries_that_balance_in_every_asset()
    {
        var entry = new JournalEntry("buy",
            Posting.Debit(Cash, 2510m),
            Posting.Credit(Bank, 2500m),
            Posting.Credit(Fees, 10m),
            Posting.Debit(Depository, 10m),
            Posting.Credit(Shares, 10m));

        Assert.Equal("buy", entry.Description);
        Assert.Equal(5, entry.Postings.Count);
    }

    [Fact]
    public void Sums_every_posting_on_each_side()
    {
        // Dwa zapisy po Wn pokrywaja jeden po Ma — liczy sie suma, nie najwieksza kwota.
        var entry = new JournalEntry("split",
            Posting.Debit(Bank, 60m),
            Posting.Debit(Fees, 40m),
            Posting.Credit(Cash, 100m));

        Assert.Equal(3, entry.Postings.Count);
        Assert.Throws<UnbalancedEntryException>(() => new JournalEntry("x",
            Posting.Debit(Bank, 60m), Posting.Debit(Fees, 40m), Posting.Credit(Cash, 60m)));
    }

    [Fact]
    public void Rejects_entries_that_do_not_balance()
    {
        var ex = Assert.Throws<UnbalancedEntryException>(() =>
            new JournalEntry("x", Posting.Debit(Bank, 100m), Posting.Credit(Cash, 99.99m)));

        Assert.Equal((Asset.Cash(Currency.Pln), 100m, 99.99m), (ex.Asset, ex.Debits, ex.Credits));
        Assert.Equal("entry does not balance in PLN: debits 100, credits 99.99", ex.Message);
    }

    [Fact]
    public void Shares_cannot_balance_cash()
    {
        var ex = Assert.Throws<UnbalancedEntryException>(() =>
            new JournalEntry("x", Posting.Debit(Bank, 10m), Posting.Credit(Shares, 10m)));

        Assert.Equal(Asset.Cash(Currency.Pln), ex.Asset);
    }

    [Fact]
    public void Needs_at_least_two_postings()
    {
        var ex = Assert.Throws<ArgumentException>(() => new JournalEntry("x", Posting.Debit(Bank, 1m)));

        Assert.Contains("at least two", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new JournalEntry("x"));
    }

    [Fact]
    public void Rejects_the_same_account_twice()
    {
        var ex = Assert.Throws<ArgumentException>(() => new JournalEntry("x",
            Posting.Debit(Bank, 1m), Posting.Credit(Bank, 1m)));

        Assert.Equal("postings", ex.ParamName);
        Assert.Contains("Bank:PLN appears more than once", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Requires_a_description(string? description) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            new JournalEntry(description!, Posting.Debit(Bank, 1m), Posting.Credit(Cash, 1m)));

    [Fact]
    public void Postings_cannot_be_changed_from_outside()
    {
        var postings = new List<Posting> { Posting.Debit(Bank, 1m), Posting.Credit(Cash, 1m) };
        var entry = new JournalEntry("x", postings);

        postings.Clear();

        Assert.Equal(2, entry.Postings.Count);
        Assert.IsNotType<List<Posting>>(entry.Postings);
    }

    [Fact]
    public void Requires_postings_without_nulls()
    {
        Assert.Equal("postings", Assert.Throws<ArgumentNullException>(() =>
            new JournalEntry("x", (IEnumerable<Posting>)null!)).ParamName);

        var ex = Assert.Throws<ArgumentException>(() => new JournalEntry("x", Posting.Debit(Bank, 1m), null!));
        Assert.Equal("postings", ex.ParamName);
        Assert.Contains("must not contain null", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_content_ignores_posting_order()
    {
        var entry = new JournalEntry("x", Posting.Debit(Bank, 1m), Posting.Credit(Cash, 1m));

        Assert.True(entry.HasSameContentAs(new JournalEntry("x", Posting.Credit(Cash, 1.00m), Posting.Debit(Bank, 1m))));
        Assert.False(entry.HasSameContentAs(new JournalEntry("y", Posting.Debit(Bank, 1m), Posting.Credit(Cash, 1m))));
        Assert.False(entry.HasSameContentAs(new JournalEntry("x", Posting.Debit(Bank, 2m), Posting.Credit(Cash, 2m))));
        Assert.False(entry.HasSameContentAs(new JournalEntry("x", Posting.Debit(Bank, 1m), Posting.Credit(Fees, 1m))));
        Assert.Throws<ArgumentNullException>(() => entry.HasSameContentAs(null!));
    }

    [Fact]
    public void Same_content_needs_the_same_number_of_postings()
    {
        var two = new JournalEntry("x", Posting.Debit(Bank, 2m), Posting.Credit(Cash, 2m));
        var three = new JournalEntry("x", Posting.Debit(Bank, 2m), Posting.Credit(Cash, 1m), Posting.Credit(Fees, 1m));

        Assert.False(two.HasSameContentAs(three));
        Assert.False(three.HasSameContentAs(two));
    }

    [Fact]
    public void Idempotency_conflict_names_the_key()
    {
        var ex = new IdempotencyConflictException("transfer-77");

        Assert.Equal("transfer-77", ex.IdempotencyKey);
        Assert.Equal("idempotency key 'transfer-77' was already used for a different entry", ex.Message);
    }

    [Fact]
    public void Description_has_a_length_limit()
    {
        var ok = new JournalEntry(new string('x', JournalEntry.MaxDescriptionLength), Posting.Debit(Bank, 1m), Posting.Credit(Cash, 1m));
        Assert.Equal(200, ok.Description.Length);

        var ex = Assert.Throws<ArgumentException>(() =>
            new JournalEntry(new string('x', 201), Posting.Debit(Bank, 1m), Posting.Credit(Cash, 1m)));
        Assert.Equal("description", ex.ParamName);
        Assert.Contains("at most 200 characters", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortfall_names_the_client_account_before_the_brokers()
    {
        // Brakuje i w banku (pierwszy zapis), i u klienta — odmowa dotyczy klienta, wiec jego konto idzie pierwsze.
        var withdrawal = new JournalEntry("withdrawal", Posting.Credit(Bank, 5m), Posting.Debit(Cash, 5m));

        var ex = Assert.Throws<InsufficientBalanceException>(() => withdrawal.EnsureNoAccountGoesNegative(_ => 0m));

        Assert.Equal(Cash, ex.Account);
    }

    [Fact]
    public void Shortfall_on_several_client_accounts_follows_document_order()
    {
        var cdrBlocked = AccountId.Blocked(Client, Asset.Shares("CDR"));
        var swap = new JournalEntry("swap",
            Posting.Debit(Shares, 1m), Posting.Credit(cdrBlocked, 1m),
            Posting.Debit(Cash, 1m), Posting.Credit(Bank, 1m));

        var ex = Assert.Throws<InsufficientBalanceException>(() => swap.EnsureNoAccountGoesNegative(_ => 0m));

        Assert.Equal(Shares, ex.Account);
    }

    [Fact]
    public void No_shortfall_when_every_account_has_enough()
    {
        var withdrawal = new JournalEntry("withdrawal", Posting.Credit(Bank, 5m), Posting.Debit(Cash, 5m));

        withdrawal.EnsureNoAccountGoesNegative(_ => 5m);
        Assert.Throws<ArgumentNullException>(() => withdrawal.EnsureNoAccountGoesNegative(null!));
    }
}
