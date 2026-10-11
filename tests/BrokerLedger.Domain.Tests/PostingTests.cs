using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class PostingTests
{
    private static readonly AccountId Bank = AccountId.Bank(Currency.Pln);
    private static readonly AccountId Cdr = AccountId.Available(ClientId.New(), Asset.Shares("CDR"));

    [Fact]
    public void Debit_and_credit_move_the_balance_in_opposite_directions()
    {
        Assert.Equal(100m, Posting.Debit(Bank, 100m).BalanceChange);
        Assert.Equal(-100m, Posting.Credit(Bank, 100m).BalanceChange);
        Assert.Equal(5m, Posting.Credit(Cdr, 5m).BalanceChange);
        Assert.Equal(-5m, Posting.Debit(Cdr, 5m).BalanceChange);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    public void Amount_must_be_positive(string amount)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Posting.Debit(Bank, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal("amount", ex.ParamName);
        Assert.Contains("must be positive", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Amount_must_fit_the_asset()
    {
        Assert.Contains("too precise for PLN", Assert.Throws<ArgumentException>(() => Posting.Debit(Bank, 0.001m)).Message, StringComparison.Ordinal);
        Assert.Contains("too precise for CDR", Assert.Throws<ArgumentException>(() => Posting.Credit(Cdr, 1.5m)).Message, StringComparison.Ordinal);
        Assert.Equal(0.01m, Posting.Debit(Bank, 0.01m).Amount);
    }

    [Fact]
    public void Requires_an_account() => Assert.Throws<ArgumentNullException>(() => Posting.Debit(null!, 1m));
}
