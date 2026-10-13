using BrokerLedger.Api.Accounts;
using BrokerLedger.Domain;

namespace BrokerLedger.Api.Market;

internal static class MarketEndpoints
{
    /// <summary>Tyle spolek miesci sie w pasku notowan; wiecej i tak nikt nie przeczyta w ruchu.</summary>
    public const int QuoteLimit = 20;

    private static readonly Asset Pln = Asset.Cash(Currency.Pln);

    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/market").RequireAuthorization().RequireRateLimiting(RateLimits.Client)
            .MapGet("/quotes", QuotesAsync);
        return app;
    }

    /// <summary>Ostatnie ceny z transakcji, ktore broker dostaje z gieldy przez Kafke — do paska notowan.</summary>
    private static async Task<IResult> QuotesAsync(IMarketPrices prices, CancellationToken ct)
    {
        var quotes = await prices.QuotesAsync(QuoteLimit, ct);
        return Results.Ok(quotes.Select(q => new
        {
            symbol = q.Symbol,
            price = Amounts.Format(q.Price, Pln),
            previousPrice = q.PreviousPrice is { } previous ? Amounts.Format(previous, Pln) : null,
            at = q.At,
            direction = q.Direction switch { > 0 => "up", < 0 => "down", _ => "flat" },
        }));
    }
}
