using System.Net;
using System.Text.Json;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BrokerLedger.Api.Tests;

/// <summary>GET /api/market/quotes: notowania z transakcji w skrzynce zdarzen gieldy.</summary>
[Collection(SharedApi.Name)]
public sealed class MarketEndToEndTests(BrokerApi api)
{
    private static readonly Uri Quotes = new("/api/market/quotes", UriKind.Relative);

    [Fact]
    public async Task Quotes_show_the_last_price_and_where_it_moved()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        await TradeAsync("AAQTA", 100m);
        await TradeAsync("AAQTA", 101.50m);
        await TradeAsync("AAQTB", 40m);

        var response = await http.GetAsync(Quotes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quotes = (await response.JsonAsync()).EnumerateArray().Where(q => q.GetProperty("symbol").GetString()!.StartsWith("AAQT", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [("AAQTA", "101.50", "100.00", "up"), ("AAQTB", "40.00", null, "flat")],
            quotes.Select(q => (q.GetProperty("symbol").GetString(), q.GetProperty("price").GetString(),
                q.GetProperty("previousPrice").GetString(), q.GetProperty("direction").GetString())));
        Assert.Equal(JsonValueKind.String, quotes[0].GetProperty("at").ValueKind);
    }

    [Fact]
    public async Task Quotes_are_for_signed_in_clients()
    {
        using var guest = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(Quotes)).StatusCode);
    }

    private Task<bool> TradeAsync(string symbol, decimal price) =>
        api.Services.GetRequiredService<IExchangeInbox>().AddAsync(new ExchangeEvent.Trade(symbol,
            new EventPosition(Random.Shared.NextInt64(1, long.MaxValue), 1), 1, 2, OrderSide.Buy, price, 1));
}
