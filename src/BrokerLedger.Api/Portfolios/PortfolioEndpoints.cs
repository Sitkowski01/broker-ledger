using System.Globalization;
using BrokerLedger.Api.Accounts;
using BrokerLedger.Api.Auth;
using BrokerLedger.Domain;

namespace BrokerLedger.Api.Portfolios;

internal static class PortfolioEndpoints
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);

    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/me").RequireAuthorization().RequireRateLimiting(RateLimits.Client)
            .MapGet("/portfolio", PortfolioAsync);
        app.MapGroup("/api/me").RequireAuthorization().RequireRateLimiting(RateLimits.Client)
            .MapGet("/performance", PerformanceAsync);
        return app;
    }

    /// <summary>Zakresy wykresu wyniku: tydzien i miesiac, jak na Pulpicie.</summary>
    private static readonly int[] PerformanceDays = [7, 30];

    /// <summary>
    /// <c>GET /api/me/performance?days=7|30</c> — wynik rachunku bez wplat i wyplat, punkt na koniec kazdego dnia
    /// i "teraz". Liczony z ksiegi i cen transakcji ze skrzynki, wiec nie ma osobnej tabeli, ktora moglaby sie rozjechac.
    /// </summary>
    private static async Task<IResult> PerformanceAsync(
        HttpContext http, ILedgerQueries queries, IMarketPrices prices, TimeProvider clock, CancellationToken ct, int? days = null)
    {
        var range = days ?? 30;
        if (!PerformanceDays.Contains(range))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["days"] = ["days must be 7 or 30"] });
        }
        var entries = await queries.ClientEntriesAsync(http.User.ClientId(), ct);
        if (entries.Count == 0)
        {
            return Results.Ok(new { points = Array.Empty<object>() });
        }
        var now = clock.GetUtcNow();
        var opened = entries.Min(e => e.CreatedAt);
        var from = opened > now.AddDays(-range) ? opened : now.AddDays(-range);
        var times = Performance.DailyTimes(from, now, Performance.ExchangeZone);
        var symbols = entries.SelectMany(e => e.Postings).Select(p => p.Account.Asset)
            .Where(a => a.Kind == AssetKind.Shares).Select(a => a.Code).Distinct().ToList();
        var priceTable = await prices.PricesAtAsync(symbols, times, ct);
        var points = Performance.Sample(entries, times, (symbol, i) => priceTable[symbol][i]);
        return Results.Ok(new
        {
            points = points.Select(p => new { at = p.At, value = Pl(p.Value), invested = Pl(p.Invested), result = Pl(p.Result) }),
        });
    }

    private static async Task<IResult> PortfolioAsync(HttpContext http, ILedgerQueries queries, IMarketPrices prices,
        IExchangeRates rates, PortfolioCurrencies currencies, CancellationToken ct)
    {
        var client = http.User.ClientId();
        // Kursy NBP z pamieci przychodza od razu; gdy trzeba o nie zapytac, niech to idzie rownolegle z baza.
        var ratesTask = rates.CurrentAsync(ct);
        var portfolio = Domain.Portfolio.FromHistory(await queries.ClientShareEntriesAsync(client, ct));
        var cash = (await queries.ClientBalancesAsync(client, ct)).Where(b => b.Account.Asset == Pln).Sum(b => b.Balance);
        var lastPrices = await prices.LastPricesAsync(portfolio.Open.Select(p => p.Symbol).ToList(), ct);
        var valuation = PortfolioValuation.Of(portfolio, Money.Pln(cash), lastPrices);
        var snapshot = await ratesTask;

        return Results.Ok(new
        {
            positions = valuation.Positions.Select(p => new
            {
                symbol = p.Position.Symbol,
                quantity = p.Position.Quantity,
                averageCost = Pl(p.Position.AverageCost!.Value),
                costBasis = Pl(p.Position.CostBasis),
                lastPrice = p.LastPrice is null ? null : Pl(p.LastPrice.Price),
                priceAt = p.LastPrice?.At,
                marketValue = Pl(p.MarketValue),
                unrealizedPnl = Pl(p.UnrealizedPnl),
                returnPercent = p.ReturnPercent is { } percent ? Pl(percent) : null,
                realizedPnl = Pl(p.Position.RealizedPnl),
            }),
            closed = portfolio.Positions.Where(p => !p.IsOpen).Select(p => new { symbol = p.Symbol, realizedPnl = Pl(p.RealizedPnl) }),
            summary = new
            {
                cash = Pl(valuation.Cash.Amount),
                sharesValue = Pl(valuation.SharesValue.Amount),
                costBasis = Pl(valuation.CostBasis.Amount),
                unrealizedPnl = Pl(valuation.UnrealizedPnl.Amount),
                realizedPnl = Pl(valuation.RealizedPnl.Amount),
                totalValue = Pl(valuation.TotalValue.Amount),
            },
            // Bez kursow portfel i tak sie otwiera — tylko bez przeliczenia na inne waluty.
            totalValueIn = snapshot is null
                ? []
                : currencies.Codes
                    .Select(c => snapshot.Table.Convert(valuation.TotalValue, c))
                    .OfType<Money>()
                    .Select(m => new { currency = m.Currency.Code, amount = Pl(m.Amount) })
                    .ToList(),
            rates = snapshot is null ? null : new
            {
                source = "NBP",
                table = snapshot.Table.Number,
                effectiveDate = snapshot.Table.EffectiveDate,
                stale = snapshot.Stale,
                mid = currencies.Codes
                    .Where(snapshot.Table.MidRates.ContainsKey)
                    .ToDictionary(c => c.Code, c => snapshot.Table.MidRates[c].ToString(CultureInfo.InvariantCulture)),
            },
        });
    }

    private static string Pl(decimal amount) => Amounts.Format(amount, Pln);
}
