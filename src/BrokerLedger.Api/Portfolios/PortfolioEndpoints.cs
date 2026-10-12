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
        return app;
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
