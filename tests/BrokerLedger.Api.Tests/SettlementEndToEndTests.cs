using System.Globalization;
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
    public async Task History_shows_trades_and_blocks_with_the_brokers_accounts()
    {
        using var http = await FundedClientAsync();
        var buy = await PlaceAsync(http, new { symbol = "PEO", side = "buy", type = "limit", limitPrice = "100.00", quantity = 10 });
        await api.TradeAsync("PEO", Stranger, ExchangeId(buy), "BUY", 90_00, 10);
        await WaitForStatusAsync(http, buy, "filled");
        var sell = await PlaceAsync(http, new { symbol = "PEO", side = "sell", type = "limit", limitPrice = "95.00", quantity = 10 });
        await api.TradeAsync("PEO", ExchangeId(sell), Stranger, "BUY", 95_00, 10);
        await WaitForStatusAsync(http, sell, "filled");

        var trades = await HistoryAsync(http, "trades");
        var blocks = await HistoryAsync(http, "blocks");

        Assert.Equal(["sale", "purchase"], trades.Select(e => Text(e, "description")));
        Assert.Equal(["block", "release", "block"], blocks.Select(e => Text(e, "description")));
        // Kupno: klient placi z blokady, bank brokera dostaje wartosc, akcje wychodza z depozytu, prowizja to przychod brokera.
        Assert.Equal(
            [("client", "blocked", "PLN", "debit", "903.51"), ("broker", "bank", "PLN", "credit", "900.00"),
             ("broker", "depository", "PEO", "debit", "10"), ("client", "available", "PEO", "credit", "10"),
             ("broker", "feeIncome", "PLN", "credit", "3.51")],
            trades[1].GetProperty("postings").EnumerateArray().Select(p => (Text(p, "owner"), Text(p, "account"),
                Text(p, "asset"), Text(p, "side"), Text(p, "amount"))));
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
    public async Task Exchange_restart_closes_the_order_and_frees_the_rest_of_the_block()
    {
        using var http = await FundedClientAsync();
        var order = await PlaceAsync(http, new { symbol = "KTY", side = "buy", type = "limit", limitPrice = "100.00", quantity = 10 });
        var exchangeId = ExchangeId(order);
        await api.TradeAsync("KTY", Stranger, exchangeId, "BUY", 100_00, 3);
        await WaitForAsync(http, order, o => o.GetProperty("filledQuantity").GetInt64() == 3);

        // Gielda startuje od pustych arkuszy. Anulowanie idzie z przebiegiem, wiec nie trafi w nowe zlecenie z tym numerem.
        api.Parkiet.Restart();
        var cancel = await http.DeleteAsync(new Uri($"/api/me/orders/{order}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        // Pierwsze zdarzenie nowego przebiegu na spolce zamyka zlecenie starego.
        await api.TradeAsync("KTY", Stranger, Stranger + 1, "BUY", 100_00, 1);

        await WaitForStatusAsync(http, order, "cancelled");
        // 3 x 100 zl + minimalna prowizja 3 zl; reszta blokady wrocila.
        Assert.Equal(("9697.00", "0.00"), await BalanceAsync(http, "PLN"));
        Assert.Equal(("3", "0"), await BalanceAsync(http, "KTY"));
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

    [Fact]
    public async Task Settled_buy_shows_up_in_the_portfolio_valued_at_the_latest_trade()
    {
        using var http = await FundedClientAsync();
        var order = await PlaceAsync(http, new { symbol = "DNP", side = "buy", type = "limit", limitPrice = "300.00", quantity = 5 });
        await api.TradeAsync("DNP", Stranger, ExchangeId(order), "BUY", 290_00, 5);
        await WaitForStatusAsync(http, order, "filled");

        // Pozniej handluja inni — broker nie rozlicza tej transakcji, ale bierze z niej cene.
        await api.TradeAsync("DNP", Stranger, Stranger + 1, "SELL", 310_00, 2);
        var position = await WaitForPortfolioAsync(http, p => p.GetProperty("lastPrice").GetString() == "310.00");

        // 5 x 290 = 1450 zl + 5,66 zl prowizji (0,39%)
        Assert.Equal(("1455.66", "291.13", "1550.00", "94.34"),
            (Text(position, "costBasis"), Text(position, "averageCost"), Text(position, "marketValue"), Text(position, "unrealizedPnl")));
    }

    /// <summary>Cena przychodzi ta sama droga co rozliczenie (Kafka → skrzynka), wiec na nia tez czekamy.</summary>
    private static async Task<JsonElement> WaitForPortfolioAsync(HttpClient http, Func<JsonElement, bool> done)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var portfolio = await (await http.GetAsync(new Uri("/api/me/portfolio", UriKind.Relative))).JsonAsync();
            if (portfolio.GetProperty("positions").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } position
                && done(position))
            {
                return position;
            }
            Assert.True(DateTime.UtcNow < deadline, $"portfolio never got there, last seen: {portfolio}");
            await Task.Delay(100);
        }
    }

    private long ExchangeId(Guid order) => api.Parkiet.ExchangeIdOf(order);

    private static async Task<List<JsonElement>> HistoryAsync(HttpClient http, string kind) =>
        (await (await http.GetAsync(new Uri($"/api/me/history?kind={kind}", UriKind.Relative))).JsonAsync())
        .GetProperty("items").EnumerateArray().ToList();

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

    /// <summary>
    /// Stan koncowy (filled / cancelled) jest zapisywany przed zwolnieniem reszty blokady — najpierw status, potem
    /// pieniadze. Dlatego po nim czekamy jeszcze, az klient (jedno zlecenie na test) nie ma nic zablokowanego.
    /// </summary>
    private static async Task<JsonElement> WaitForStatusAsync(HttpClient http, Guid order, string status)
    {
        var reached = await WaitForAsync(http, order, o => Text(o, "status") == status);
        if (status is "filled" or "cancelled")
        {
            await WaitForNothingBlockedAsync(http);
        }
        return reached;
    }

    private static async Task WaitForNothingBlockedAsync(HttpClient http)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var balances = await (await http.GetAsync(new Uri("/api/me/balances", UriKind.Relative))).JsonAsync();
            if (balances.EnumerateArray().All(b => decimal.Parse(Text(b, "blocked")!, CultureInfo.InvariantCulture) == 0m))
            {
                return;
            }
            Assert.True(DateTime.UtcNow < deadline, $"block never released, last seen: {balances}");
            await Task.Delay(100);
        }
    }

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
