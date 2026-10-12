namespace BrokerLedger.Domain;

/// <summary>
/// Otwarta pozycja wyceniona po ostatniej cenie. Spolka bez zadnej transakcji nie ma ceny — wtedy wycena po koszcie
/// (<see cref="LastPrice"/> = null, zysk 0): lepiej pokazac to, co klient zaplacil, niz zero albo zgadywac.
/// </summary>
public sealed record ValuedPosition(Position Position, MarketPrice? LastPrice)
{
    public decimal MarketValue => LastPrice is null ? Position.CostBasis : LastPrice.Price * Position.Quantity;

    public decimal UnrealizedPnl => MarketValue - Position.CostBasis;

    /// <summary>Stopa zwrotu w procentach, do setnych. Brak przy koszcie 0 (akcje przeniesione z innego biura).</summary>
    public decimal? ReturnPercent => Position.CostBasis == 0m
        ? null
        : decimal.Round(UnrealizedPnl / Position.CostBasis * 100m, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Portfel wyceniony na teraz: otwarte pozycje, gotowka i sumy (wszystko w zlotowkach).</summary>
public sealed class PortfolioValuation
{
    private PortfolioValuation(IReadOnlyList<ValuedPosition> positions, Portfolio portfolio, Money cash)
    {
        Positions = positions;
        Portfolio = portfolio;
        Cash = cash;
    }

    public IReadOnlyList<ValuedPosition> Positions { get; }
    public Portfolio Portfolio { get; }

    /// <summary>Gotowka klienta: wolna i zablokowana pod zlecenia kupna.</summary>
    public Money Cash { get; }

    public Money SharesValue => Money.Pln(Positions.Sum(p => p.MarketValue));
    public Money CostBasis => Money.Pln(Positions.Sum(p => p.Position.CostBasis));
    public Money UnrealizedPnl => Money.Pln(Positions.Sum(p => p.UnrealizedPnl));
    public Money RealizedPnl => Money.Pln(Portfolio.RealizedPnl);

    /// <summary>Ile klient by mial, gdyby sprzedal wszystko po ostatnich cenach (bez prowizji od sprzedazy).</summary>
    public Money TotalValue => Cash + SharesValue;

    public static PortfolioValuation Of(Portfolio portfolio, Money cash, IReadOnlyDictionary<string, MarketPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(cash);
        ArgumentNullException.ThrowIfNull(prices);
        if (cash.Currency != Currency.Pln)
        {
            throw new ArgumentException($"cash must be in PLN, got {cash.Currency}", nameof(cash));
        }
        var positions = portfolio.Open
            .Select(p => new ValuedPosition(p, prices.GetValueOrDefault(p.Symbol)))
            .ToList();
        return new PortfolioValuation(positions, portfolio, cash);
    }
}
