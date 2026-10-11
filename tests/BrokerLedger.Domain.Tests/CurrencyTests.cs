using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class CurrencyTests
{
    [Theory]
    [InlineData("PLN")]
    [InlineData("USD")]
    public void Accepts_iso_codes(string code) => Assert.Equal(code, new Currency(code).ToString());

    [Theory]
    [InlineData("pln")]
    [InlineData("PL")]
    [InlineData("PLNX")]
    [InlineData("P1N")]
    [InlineData("ŁÓD")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_anything_else(string? code)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Currency(code!));

        Assert.Equal("code", ex.ParamName);
        Assert.Contains("3 upper-case letters", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Equal_by_code() => Assert.Equal(Currency.Pln, new Currency("PLN"));
}
