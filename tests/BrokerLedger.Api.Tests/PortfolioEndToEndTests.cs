using System.Net;
using System.Text.Json;
using BrokerLedger.Domain;
using BrokerLedger.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BrokerLedger.Api.Tests;

/// <summary>
/// GET /api/me/portfolio na prawdziwej bazie: dokumenty w ksiedze, transakcje gieldy w skrzynce, kursy z atrapy NBP.
/// Zakupy ksiegujemy bezposrednio — droge przez gielde i Kafke sprawdza <see cref="SettlementEndToEndTests"/>.
/// </summary>
[Collection(SharedApi.Name)]
public sealed class PortfolioEndToEndTests(BrokerApi api)
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private static readonly Uri Portfolio = new("/api/me/portfolio", UriKind.Relative);

    [Fact]
    public async Task Portfolio_values_positions_at_the_last_trade_and_in_other_currencies()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        (await http.PostMoneyAsync("/api/me/deposits", "dep", "10000")).EnsureSuccessStatusCode();
        var client = Clients.ClientIdOf(http);
        await BuyAsync(client, "PFCDR", 10, 1000m, 3.90m);
        await BuyAsync(client, "PFPKO", 4, 200m, 3m);
        await SellAsync(client, "PFPKO", 4, 240m, 3m);
        await TradeAsync("PFCDR", 108m);
        await TradeAsync("PFCDR", 110.50m);

        var portfolio = await GetAsync(http);

        var cdr = Assert.Single(portfolio.GetProperty("positions").EnumerateArray());
        Assert.Equal("PFCDR", Text(cdr, "symbol"));
        Assert.Equal(10, cdr.GetProperty("quantity").GetInt64());
        Assert.Equal(["100.39", "1003.90", "110.50", "1105.00", "101.10", "10.07", "0.00"],
            Texts(cdr, "averageCost", "costBasis", "lastPrice", "marketValue", "unrealizedPnl", "returnPercent", "realizedPnl"));
        Assert.Equal(JsonValueKind.String, cdr.GetProperty("priceAt").ValueKind);

        var pko = Assert.Single(portfolio.GetProperty("closed").EnumerateArray());
        Assert.Equal(["PFPKO", "34.00"], Texts(pko, "symbol", "realizedPnl"));

        // 10 000 - 1003,90 - 203 + 237 = 9030,10 gotowki
        Assert.Equal(["9030.10", "1105.00", "1003.90", "101.10", "34.00", "10135.10"],
            Texts(portfolio.GetProperty("summary"), "cash", "sharesValue", "costBasis", "unrealizedPnl", "realizedPnl", "totalValue"));

        // GBP i CHF sa w konfiguracji, ale nie w tabeli — pomijamy je, zamiast zgadywac kurs.
        Assert.Equal(["EUR 2384.73", "USD 2533.78"], portfolio.GetProperty("totalValueIn").EnumerateArray()
            .Select(v => $"{Text(v, "currency")} {Text(v, "amount")}"));
        var rates = portfolio.GetProperty("rates");
        Assert.Equal(["NBP", "198/A/NBP/2026", "2026-10-09"], Texts(rates, "source", "table", "effectiveDate"));
        Assert.False(rates.GetProperty("stale").GetBoolean());
        Assert.Equal(["EUR=4.25", "USD=4.0"], rates.GetProperty("mid").EnumerateObject().Select(r => $"{r.Name}={r.Value.GetString()}"));
    }

    [Fact]
    public async Task New_client_has_an_empty_portfolio()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var portfolio = await GetAsync(http);

        Assert.Empty(portfolio.GetProperty("positions").EnumerateArray());
        Assert.Empty(portfolio.GetProperty("closed").EnumerateArray());
        Assert.All(portfolio.GetProperty("summary").EnumerateObject(), total => Assert.Equal("0.00", total.Value.GetString()));
    }

    [Fact]
    public async Task Company_without_trades_is_valued_at_cost()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        (await http.PostMoneyAsync("/api/me/deposits", "dep", "1000")).EnsureSuccessStatusCode();
        await BuyAsync(Clients.ClientIdOf(http), "PFNONE", 3, 90m, 3m);

        var position = Assert.Single((await GetAsync(http)).GetProperty("positions").EnumerateArray());

        Assert.Equal(JsonValueKind.Null, position.GetProperty("lastPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, position.GetProperty("priceAt").ValueKind);
        Assert.Equal(["93.00", "0.00", "0.00"], Texts(position, "marketValue", "unrealizedPnl", "returnPercent"));
    }

    [Fact]
    public async Task Free_shares_have_no_return_percent()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        await Ledger.PostAsync($"in-{Guid.NewGuid()}", JournalEntries.SharesTransferIn(Clients.ClientIdOf(http), "PFFREE", 2));

        var position = Assert.Single((await GetAsync(http)).GetProperty("positions").EnumerateArray());

        Assert.Equal(["0.00", "0.00"], Texts(position, "averageCost", "costBasis"));
        Assert.Equal(JsonValueKind.Null, position.GetProperty("returnPercent").ValueKind);
    }

    [Fact]
    public async Task Portfolio_opens_without_exchange_rates_when_nbp_is_down()
    {
        var nbp = new FakeNbp().Down();
        using var app = WithNbp(nbp);
        using var http = await Clients.RegisterAndLoginAsync(app);

        var response = await http.GetAsync(Portfolio);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var portfolio = await response.JsonAsync();
        Assert.Equal(JsonValueKind.Null, portfolio.GetProperty("rates").ValueKind);
        Assert.Empty(portfolio.GetProperty("totalValueIn").EnumerateArray());
        // Trzy proby (pierwsza i dwa ponowienia), potem przerwa — nastepny portfel nie czeka na NBP.
        await http.GetAsync(Portfolio);
        Assert.Equal(3, nbp.Calls);
    }

    [Fact]
    public async Task Last_known_rates_are_marked_stale_when_nbp_stops_answering()
    {
        var nbp = new FakeNbp();
        using var app = WithNbp(nbp, ("ExchangeRates:CacheFor", "00:00:00.001"));
        using var http = await Clients.RegisterAndLoginAsync(app);
        Assert.False((await GetAsync(http)).GetProperty("rates").GetProperty("stale").GetBoolean());

        nbp.Down();
        var portfolio = await GetAsync(http);

        Assert.True(portfolio.GetProperty("rates").GetProperty("stale").GetBoolean());
        Assert.Equal("198/A/NBP/2026", Text(portfolio.GetProperty("rates"), "table"));
        Assert.Equal(2, portfolio.GetProperty("totalValueIn").GetArrayLength());
    }

    [Fact]
    public async Task Currencies_follow_the_configuration_once_each()
    {
        using var app = WithNbp(new FakeNbp(), ("ExchangeRates:Currencies:0", "USD"), ("ExchangeRates:Currencies:1", "USD"),
            ("ExchangeRates:Currencies:2", "JPY"));
        using var http = await Clients.RegisterAndLoginAsync(app);

        var portfolio = await GetAsync(http);

        Assert.Equal(["USD", "JPY"], portfolio.GetProperty("totalValueIn").EnumerateArray().Select(v => Text(v, "currency")));
        Assert.Equal(["USD", "JPY"], portfolio.GetProperty("rates").GetProperty("mid").EnumerateObject().Select(r => r.Name));
    }

    [Fact]
    public async Task Portfolio_needs_a_token()
    {
        using var http = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(Portfolio)).StatusCode);
    }

    private ILedgerStore Ledger => api.Services.GetRequiredService<ILedgerStore>();

    /// <summary>Wlasna instancja aplikacji (i wlasna pamiec kursow) z podanym NBP.</summary>
    private WebApplicationFactory<Program> WithNbp(FakeNbp nbp, params (string Key, string Value)[] settings) =>
        api.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
            builder.ConfigureServices(services =>
                services.AddHttpClient(LedgerInfrastructure.NbpHttpClient).ConfigurePrimaryHttpMessageHandler(nbp.Handler));
        });

    private async Task BuyAsync(ClientId client, string symbol, long quantity, decimal value, decimal fee)
    {
        await Ledger.PostAsync($"block-{Guid.NewGuid()}", JournalEntries.Block(client, Pln, value + fee));
        await Ledger.PostAsync($"buy-{Guid.NewGuid()}", JournalEntries.Purchase(client, symbol, quantity, value, fee));
    }

    private async Task SellAsync(ClientId client, string symbol, long quantity, decimal value, decimal fee)
    {
        await Ledger.PostAsync($"block-{Guid.NewGuid()}", JournalEntries.Block(client, Asset.Shares(symbol), quantity));
        await Ledger.PostAsync($"sell-{Guid.NewGuid()}", JournalEntries.Sale(client, symbol, quantity, value, fee));
    }

    /// <summary>Transakcja innych uczestnikow gieldy — broker zna ja ze skrzynki, choc nie musi jej rozliczac.</summary>
    private Task<bool> TradeAsync(string symbol, decimal price) =>
        api.Services.GetRequiredService<IExchangeInbox>().AddAsync(new ExchangeEvent.Trade(symbol,
            new EventPosition(Random.Shared.NextInt64(1, long.MaxValue), 1), 1, 2, OrderSide.Buy, price, 1));

    private static async Task<JsonElement> GetAsync(HttpClient http)
    {
        var response = await http.GetAsync(Portfolio);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.JsonAsync();
    }

    private static string? Text(JsonElement json, string property) => json.GetProperty(property).GetString();

    private static string[] Texts(JsonElement json, params string[] properties) => [.. properties.Select(p => json.GetProperty(p).ToString())];
}
