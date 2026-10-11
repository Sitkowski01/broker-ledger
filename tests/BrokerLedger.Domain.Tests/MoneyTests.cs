using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class MoneyTests
{
    private static readonly Currency Eur = new("EUR");

    [Theory]
    [InlineData("250.40")]
    [InlineData("0.01")]
    [InlineData("-7")]
    [InlineData("0")]
    public void Accepts_whole_grosze(string amount)
    {
        var money = Money.Pln(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(Currency.Pln, money.Currency);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("250.405")]
    [InlineData("-0.009")]
    public void Rejects_fractions_of_a_grosz(string amount)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Money.Pln(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal("amount", ex.ParamName);
        Assert.Contains(amount, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Adds_and_subtracts_in_the_same_currency()
    {
        Assert.Equal(Money.Pln(300.50m), Money.Pln(250.40m) + Money.Pln(50.10m));
        Assert.Equal(Money.Pln(-0.01m), Money.Pln(10m) - Money.Pln(10.01m));
        Assert.Equal(Money.Pln(-3m), -Money.Pln(3m));
    }

    [Fact]
    public void Requires_a_currency() =>
        Assert.Throws<ArgumentNullException>(() => Money.Of(1m, null!));

    [Fact]
    public void Rejects_null_operands()
    {
        Assert.Throws<ArgumentNullException>(() => Money.Pln(1m) + null!);
        Assert.Throws<ArgumentNullException>(() => null! - Money.Pln(1m));
        Assert.Throws<ArgumentNullException>(() => -(Money)null!);
        Assert.Throws<ArgumentNullException>(() => Money.Pln(1m).CompareTo(null));
    }

    [Fact]
    public void Refuses_to_mix_currencies()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Money.Pln(1m) + Money.Of(1m, Eur));
        Assert.Equal("cannot combine PLN with EUR", ex.Message);
        Assert.Throws<InvalidOperationException>(() => Money.Pln(1m) - Money.Of(1m, Eur));
        Assert.Throws<InvalidOperationException>(() => Money.Pln(1m) < Money.Of(2m, Eur));
    }

    [Fact]
    public void Equal_regardless_of_trailing_zeros()
    {
        Assert.Equal(Money.Pln(250.4m), Money.Pln(250.40m));
        Assert.Equal(Money.Pln(250.4m).GetHashCode(), Money.Pln(250.40m).GetHashCode());
        Assert.NotEqual(Money.Pln(1m), Money.Of(1m, Eur));
    }

    [Fact]
    public void Compares_amounts()
    {
        var less = Money.Pln(9.99m);
        var more = Money.Pln(10m);

        Assert.True(less < more);
        Assert.False(more < less);
        Assert.False(more < Money.Pln(10m));
        Assert.True(more > less);
        Assert.False(less > more);
        Assert.False(more > Money.Pln(10m));
        Assert.True(less <= more);
        Assert.True(more <= Money.Pln(10.00m));
        Assert.False(more <= less);
        Assert.True(more >= less);
        Assert.True(more >= Money.Pln(10.00m));
        Assert.False(less >= more);
        Assert.Equal(0, more.CompareTo(Money.Pln(10.00m)));
    }

    [Fact]
    public void Knows_zero_and_negative()
    {
        Assert.True(Money.Zero(Eur).IsZero);
        Assert.False(Money.Pln(0.01m).IsZero);
        Assert.True(Money.Pln(-0.01m).IsNegative);
        Assert.False(Money.Pln(0m).IsNegative);
        Assert.False(Money.Pln(0.01m).IsNegative);
    }

    [Fact]
    public void Prints_with_two_decimals_and_currency()
    {
        Assert.Equal("250.40 PLN", Money.Pln(250.4m).ToString());
        Assert.Equal("-3.00 EUR", Money.Of(-3m, Eur).ToString());
    }
}
