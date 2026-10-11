using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class AssetTests
{
    [Fact]
    public void Cash_counts_grosze_and_shares_count_whole_pieces()
    {
        var pln = Asset.Cash(Currency.Pln);
        var cdr = Asset.Shares("CDR");

        Assert.Equal((AssetKind.Cash, "PLN", 2), (pln.Kind, pln.Code, pln.Scale));
        Assert.Equal((AssetKind.Shares, "CDR", 0), (cdr.Kind, cdr.Code, cdr.Scale));
        Assert.True(pln.Fits(250.40m));
        Assert.False(pln.Fits(250.405m));
        Assert.True(cdr.Fits(10m));
        Assert.False(cdr.Fits(10.5m));
    }

    [Fact]
    public void Cash_and_shares_with_the_same_code_are_different_assets()
    {
        Assert.NotEqual(Asset.Cash(new Currency("ABC")), Asset.Shares("ABC"));
        Assert.Equal(Asset.Shares("CDR"), Asset.Shares("CDR"));
    }

    [Theory]
    [InlineData("CDR")]
    [InlineData("P")]
    [InlineData("WIG20FUT2612")]
    public void Accepts_exchange_symbols(string symbol) => Assert.Equal(symbol, Asset.Shares(symbol).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("cdr")]
    [InlineData("CD-R")]
    [InlineData("WIG20FUT26123")]
    [InlineData(null)]
    public void Rejects_other_symbols(string? symbol)
    {
        var ex = Assert.Throws<ArgumentException>(() => Asset.Shares(symbol!));

        Assert.Equal("symbol", ex.ParamName);
        Assert.Contains("1-12 upper-case letters or digits", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cash_requires_a_currency() => Assert.Throws<ArgumentNullException>(() => Asset.Cash(null!));
}
