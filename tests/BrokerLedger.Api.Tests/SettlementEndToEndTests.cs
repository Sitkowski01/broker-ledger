using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using static BrokerLedger.Api.Tests.FakeParkiet;

namespace BrokerLedger.Api.Tests;

/// <summary>
/// Cala droga transakcji: zlecenie przez HTTP → Parkiet (w pamieci) → zdarzenie na prawdziwej Kafce → skrzynka
/// i rozliczenie w PostgreSQL → salda przez HTTP. Kazdy test ma wlasna spolke, zeby zlecenie "w drodze"
/// jednego testu nie wstrzymywalo rozliczen drugiego.
/// </summary>
[Collection(SharedKafkaApi.Name)]
public sealed class SettlementEndToEndTests(KafkaBrokerApi api)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Numer zlecenia kogos spoza brokera — po drugiej stronie transakcji.</summary>
    private const long Stranger = 900_000;

    [Fact]
    public async Task Filled_buy_brings_shares_and_returns_the_unused_block()
    {
        using var http = await FundedClientAsync();
        var order = await PlaceAsync(http, new { symbol = "KGH", side = "buy", type = "limit", limitPrice = "150.00", quantity = 10 });

        // Sprzedajacy czekal w arkuszu po 140 zl: kupujemy taniej niz limit.
        await api.TradeAsync("KGH", Stranger, ExchangeId(order), "BUY", 140_00, 10);

        var filled = await WaitForStatusAsync(http, order, "filled");
        Assert.Equal((10L, "1400.00", "5.46"), (filled.GetProperty("filledQuantity").GetInt64(), Text(filled, "filledValue"), Text(filled, "commission")));
        Assert.Equal(("8594.54", "0.00"), await BalanceAsync(http, "PLN"));
        Assert.Equal(("10", "0"), await BalanceAsync(http, "KGH"));
    }

    [Fact]
    public async Task Partly_filled_sell_cancelled_on_request_gets_the_rest_of_the_shares_back()
    {
        using var http = await FundedClientAsync(shares: ("PKO", 20));
        var order = await PlaceAsync(http, new { symbol = "PKO", side = "sell", type = "limit", limitPrice = "60.00", quantity = 20 });
        await api.TradeAsync("PKO", ExchangeId(order), Stranger, "BUY", 60_00, 8);
        await WaitForAsync(http, order, o => o.GetProperty("filledQuantity").GetInt64() == 8);

        var cancel = await http.DeleteAsync(new Uri($"/api/me/orders/{order}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        await api.CancelledAsync("PKO", ExchangeId(order), 12, "REQUESTED");

        await WaitForStatusAsync(http, order, "cancelled");
        Assert.Equal(("12", "0"), await BalanceAsync(http, "PKO"));
        // 8 x 60 = 480 zl minus minimalna prowizja 3 zl.
        Assert.Equal(("10477.00", "0.00"), await BalanceAsync(http, "PLN"));
    }

    [Fact]
    public async Task Market_sell_without_buyers_returns_the_shares()
    {
        using var http = await FundedClientAsync(shares: ("ALE", 5));
        var order = await PlaceAsync(http, new { symbol = "ALE", side = "sell", type = "market", quantity = 5 });

        await api.CancelledAsync("ALE", ExchangeId(order), 5, "NO_LIQUIDITY");

        await WaitForStatusAsync(http, order, "cancelled");
        Assert.Equal(("5", "0"), await BalanceAsync(http, "ALE"));
    }

    [Fact]
    public async Task Repeated_event_is_settled_once()
    {
        using var http = await FundedClientAsync();
        var order = await PlaceAsync(http, new { symbol = "LPP", side = "buy", type = "limit", limitPrice = "100.00", quantity = 4 });
        var sequence = await api.TradeAsync("LPP", Stranger, ExchangeId(order), "BUY", 100_00, 1);
        await api.TradeAsync("LPP", Stranger, ExchangeId(order), "BUY", 100_00, 1, repeat: sequence);
        await api.TradeAsync("LPP", Stranger, ExchangeId(order), "BUY", 100_00, 3);

        await WaitForStatusAsync(http, order, "filled");

        Assert.Equal(("4", "0"), await BalanceAsync(http, "LPP"));
        Assert.Equal(("9597.00", "0.00"), await BalanceAsync(http, "PLN"));
    }

    [Fact]
    public async Task Trade_for_an_order_the_broker_has_no_number_for_waits_for_the_reconciler()
    {
        using var http = await FundedClientAsync();
        api.Parkiet.Then(Fault.PlacedButTimedOut);
        var order = await PlaceAsync(http, new { symbol = "CCC", side = "buy", type = "limit", limitPrice = "50.00", quantity = 2 }, HttpStatusCode.Accepted);

        // Gielda zna numer, broker jeszcze nie — transakcja nie moze byc uznana za "nie nasza".
        await api.TradeAsync("CCC", Stranger, api.Parkiet.ExchangeIdOf(order), "BUY", 50_00, 2);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal("unknown", Text(await GetAsync(http, order), "status"));

        await api.ReconcileAsync();

        await WaitForStatusAsync(http, order, "filled");
        Assert.Equal(("2", "0"), await BalanceAsync(http, "CCC"));
    }

    private long ExchangeId(Guid order) => api.Parkiet.ExchangeIdOf(order);

    private async Task<HttpClient> FundedClientAsync((string Symbol, long Quantity)? shares = null)
    {
        var http = await Clients.RegisterAndLoginAsync(api);
        (await http.PostMoneyAsync("/api/me/deposits", "dep", "10000")).EnsureSuccessStatusCode();
        if (shares is { } transfer)
        {
            await api.Services.GetRequiredService<ILedgerStore>().PostAsync($"shares-{Guid.NewGuid()}",
                JournalEntries.SharesTransferIn(Clients.ClientIdOf(http), transfer.Symbol, transfer.Quantity));
        }
        return http;
    }

    private static async Task<Guid> PlaceAsync(HttpClient http, object body, HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await http.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        return Guid.Parse(Text(await response.JsonAsync(), "id")!);
    }

    private static async Task<JsonElement> GetAsync(HttpClient http, Guid order) =>
        await (await http.GetAsync(new Uri($"/api/me/orders/{order}", UriKind.Relative))).JsonAsync();

    private static Task<JsonElement> WaitForStatusAsync(HttpClient http, Guid order, string status) =>
        WaitForAsync(http, order, o => Text(o, "status") == status);

    /// <summary>Rozliczenie idzie w tle (Kafka → skrzynka → przebieg co 50 ms), wiec czekamy na jego efekt.</summary>
    private static async Task<JsonElement> WaitForAsync(HttpClient http, Guid order, Func<JsonElement, bool> done)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var current = await GetAsync(http, order);
            if (done(current))
            {
                return current;
            }
            Assert.True(DateTime.UtcNow < deadline, $"order never got there, last seen: {current}");
            await Task.Delay(100);
        }
    }

    private static async Task<(string? Available, string? Blocked)> BalanceAsync(HttpClient http, string asset)
    {
        var balance = (await (await http.GetAsync(new Uri("/api/me/balances", UriKind.Relative))).JsonAsync())
            .EnumerateArray().Single(b => b.GetProperty("asset").GetString() == asset);
        return (Text(balance, "available"), Text(balance, "blocked"));
    }

    private static string? Text(JsonElement json, string property) => json.GetProperty(property).GetString();
}
