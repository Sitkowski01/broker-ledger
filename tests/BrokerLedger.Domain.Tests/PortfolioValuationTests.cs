using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class PortfolioValuationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly ClientId _client = ClientId.New();

    private Portfolio PortfolioOf(params JournalEntry[] entries) =>
        Portfolio.FromHistory(entries.Select((e, i) => PortfolioTests.AsSeenBy(_client, e, i + 1)));

    [Fact]
    public void Open_positions_are_valued_at_the_last_price()
    {
        var portfolio = PortfolioOf(
            JournalEntries.Purchase(_client, "CDR", 10, 1000m, 3.90m),
            JournalEntries.Purchase(_client, "PKO", 4, 200m, 3m));
        var prices = new Dictionary<string, MarketPrice> { ["CDR"] = new(110.50m, Noon), ["PKO"] = new(45m, Noon) };

        var valuation = PortfolioValuation.Of(portfolio, Money.Pln(500m), prices);

        var cdr = valuation.Positions[0];
        Assert.Equal((1105m, 101.10m, 10.07m), (cdr.MarketValue, cdr.UnrealizedPnl, cdr.ReturnPercent));
        var pko = valuation.Positions[1];
        Assert.Equal((180m, -23m, -11.33m), (pko.MarketValue, pko.UnrealizedPnl, pko.ReturnPercent));
        Assert.Equal(new MarketPrice(45m, Noon), pko.LastPrice);
        Assert.Equal(Money.Pln(1285m), valuation.SharesValue);
        Assert.Equal(Money.Pln(1206.90m), valuation.CostBasis);
        Assert.Equal(Money.Pln(78.10m), valuation.UnrealizedPnl);
        Assert.Equal(Money.Pln(1785m), valuation.TotalValue);
        Assert.Equal(Money.Pln(500m), valuation.Cash);
    }

    [Fact]
    public void Position_without_any_trade_is_valued_at_cost()
    {
        var portfolio = PortfolioOf(JournalEntries.Purchase(_client, "ALE", 3, 90m, 3m));

        var position = Assert.Single(PortfolioValuation.Of(portfolio, Money.Pln(0m), new Dictionary<string, MarketPrice>()).Positions);

        Assert.Null(position.LastPrice);
        Assert.Equal((93m, 0m, 0m), (position.MarketValue, position.UnrealizedPnl, position.ReturnPercent));
    }

    [Fact]
    public void Free_shares_have_no_return_percent()
    {
        var portfolio = PortfolioOf(JournalEntries.SharesTransferIn(_client, "KGH", 2));

        var position = Assert.Single(PortfolioValuation.Of(portfolio, Money.Pln(0m),
            new Dictionary<string, MarketPrice> { ["KGH"] = new(150m, Noon) }).Positions);

        Assert.Equal((300m, 300m), (position.MarketValue, position.UnrealizedPnl));
        Assert.Null(position.ReturnPercent);
    }

    [Fact]
    public void Closed_positions_are_not_valued_but_keep_their_realized_result()
    {
        var portfolio = PortfolioOf(
            JournalEntries.Purchase(_client, "CDR", 1, 100m, 3m),
            JournalEntries.Block(_client, Asset.Shares("CDR"), 1),
            JournalEntries.Sale(_client, "CDR", 1, 120m, 3m));

        var valuation = PortfolioValuation.Of(portfolio, Money.Pln(117m),
            new Dictionary<string, MarketPrice> { ["CDR"] = new(130m, Noon) });

        Assert.Empty(valuation.Positions);
        Assert.Equal(Money.Pln(14m), valuation.RealizedPnl);
        Assert.Equal(Money.Pln(117m), valuation.TotalValue);
    }

    [Fact]
    public void Cash_must_be_in_zloty() =>
        Assert.Contains("cash must be in PLN, got EUR", Assert.Throws<ArgumentException>(() =>
            PortfolioValuation.Of(PortfolioOf(), Money.Of(1m, new Currency("EUR")), new Dictionary<string, MarketPrice>())).Message, StringComparison.Ordinal);

    [Fact]
    public void Nulls_are_refused()
    {
        var prices = new Dictionary<string, MarketPrice>();
        Assert.Throws<ArgumentNullException>(() => PortfolioValuation.Of(null!, Money.Pln(0m), prices));
        Assert.Throws<ArgumentNullException>(() => PortfolioValuation.Of(PortfolioOf(), null!, prices));
        Assert.Throws<ArgumentNullException>(() => PortfolioValuation.Of(PortfolioOf(), Money.Pln(0m), null!));
    }
}

public class ExchangeRateTableTests
{
    private static readonly Currency Eur = new("EUR");
    private static readonly Currency Usd = new("USD");

    private static ExchangeRateTable Table() =>
        new("198/A/NBP/2026", new DateOnly(2026, 10, 9), new Dictionary<Currency, decimal> { [Eur] = 4.2512m, [Usd] = 3.6m });

    [Theory]
    [InlineData("1000.00", "EUR", "235.23")]
    [InlineData("1000.00", "USD", "277.78")]
    [InlineData("0.00", "EUR", "0.00")]
    // 0,03 / 4,2512 = 0,0071 -> 0,01: przelicznik nie gubi drobnych kwot do zera
    [InlineData("0.03", "EUR", "0.01")]
    public void Zloty_amount_is_converted_at_the_mid_rate_to_whole_cents(string pln, string currency, string expected)
    {
        var converted = Table().Convert(Money.Pln(decimal.Parse(pln, System.Globalization.CultureInfo.InvariantCulture)), new Currency(currency));

        Assert.Equal(Money.Of(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), new Currency(currency)), converted);
    }

    [Fact]
    public void Currency_missing_from_the_table_gives_nothing() =>
        Assert.Null(Table().Convert(Money.Pln(10m), new Currency("JPY")));

    [Fact]
    public void Only_zloty_can_be_converted() =>
        Assert.Contains("only PLN amounts can be converted, got EUR", Assert.Throws<ArgumentException>(() => Table().Convert(Money.Of(1m, Eur), Usd)).Message, StringComparison.Ordinal);

    [Fact]
    public void Table_keeps_what_nbp_published()
    {
        var table = Table();

        Assert.Equal(("198/A/NBP/2026", new DateOnly(2026, 10, 9), 4.2512m), (table.Number, table.EffectiveDate, table.MidRates[Eur]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rates_must_be_positive(int rate) =>
        Assert.Contains("exchange rates must be positive", Assert.Throws<ArgumentException>(() =>
            new ExchangeRateTable("1/A/NBP/2026", new DateOnly(2026, 1, 2), new Dictionary<Currency, decimal> { [Eur] = rate })).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Table_needs_a_number(string number) =>
        Assert.Throws<ArgumentException>(() => new ExchangeRateTable(number, new DateOnly(2026, 1, 2), new Dictionary<Currency, decimal>()));

    [Fact]
    public void Nulls_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new ExchangeRateTable("1/A/NBP/2026", new DateOnly(2026, 1, 2), null!));
        Assert.Throws<ArgumentNullException>(() => Table().Convert(null!, Eur));
        Assert.Equal("target", Assert.Throws<ArgumentNullException>(() => Table().Convert(Money.Pln(1m), null!)).ParamName);
    }
}
