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
}
