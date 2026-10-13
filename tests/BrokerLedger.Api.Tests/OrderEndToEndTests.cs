using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using static BrokerLedger.Api.Tests.FakeParkiet;

namespace BrokerLedger.Api.Tests;

/// <summary>
/// Zlecenie przez HTTP: blokada w ksiedze (PostgreSQL) → gielda (Parkiet w pamieci) → stan zlecenia.
/// Kupno 10 CDR po 250 zl blokuje 2500 zl + 9,75 zl prowizji.
/// </summary>
[Collection(SharedApi.Name)]
public sealed partial class OrderEndToEndTests(BrokerApi api)
{
    private const string Blocked = "2509.75";

    private static object Buy(string price = "250.00", long quantity = 10) =>
        new { symbol = "CDR", side = "buy", type = "limit", limitPrice = price, quantity };

    [Fact]
    public async Task Accepted_buy_blocks_money_and_goes_to_the_exchange()
    {
        using var http = await FundedClientAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await PlaceAsync(http, "o-1", Buy());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.JsonAsync();
        Assert.Equal(("accepted", "PLN", Blocked), (Text(order, "status"), Text(order, "blockedAsset"), Text(order, "blocked")));
        // Czas zlozenia do kolumny "Zlozone" na liscie zlecen we froncie.
        Assert.InRange(order.GetProperty("createdAt").GetDateTimeOffset(), before, DateTimeOffset.UtcNow.AddSeconds(1));
        var sent = Assert.Single(api.Parkiet.Placed, p => Cid(p) == Guid.Parse(Text(order, "id")!).ToString("N"));
        Assert.Equal(("BUY", "LIMIT", "250.00", 10L), (Text(sent, "side"), Text(sent, "type"), Text(sent, "price"), sent.GetProperty("quantity").GetInt64()));
        Assert.Equal(("7490.25", Blocked), await BalanceAsync(http));
        // Dokument blokady w historii prowadzi do zlecenia, ktore go wywolalo.
        var block = (await http.GetFromJsonAsync<JsonElement>("/api/me/history")).GetProperty("items")[0];
        Assert.Equal(("block", Text(order, "id")), (Text(block, "description"), Text(block, "orderId")));
    }

    [Fact]
    public async Task Not_enough_money_is_422_and_never_reaches_the_exchange()
    {
        using var http = await FundedClientAsync(deposit: "100");
        var callsBefore = api.Parkiet.PlaceCalls;

        var response = await PlaceAsync(http, "o-1", Buy());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var order = await response.JsonAsync();
        Assert.Equal(("rejected", "insufficient funds"), (Text(order, "status"), Text(order, "rejectionReason")));
        Assert.Equal(callsBefore, api.Parkiet.PlaceCalls);
        Assert.Equal(("100.00", "0.00"), await BalanceAsync(http));
    }

    [Theory]
    [InlineData(Fault.Busy, "CDR: queue full")]
    [InlineData(Fault.BadRequest, "limit order requires a price")]
    public async Task Exchange_refusal_returns_the_money(Fault fault, string reason)
    {
        using var http = await FundedClientAsync();
        api.Parkiet.Then(fault);

        var response = await PlaceAsync(http, "o-1", Buy());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(reason, Text(await response.JsonAsync(), "rejectionReason"));
        Assert.Equal(("10000.00", "0.00"), await BalanceAsync(http));
    }

    [Theory]
    [InlineData(Fault.PlacedButTimedOut)]
    [InlineData(Fault.Silence)]
    [InlineData(Fault.NetworkError)]
    public async Task No_answer_keeps_the_block_and_the_reconciler_explains_it(Fault fault)
    {
        using var http = await FundedClientAsync();
        api.Parkiet.Then(fault);

        var response = await PlaceAsync(http, "o-1", Buy());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = Text(await response.JsonAsync(), "id");
        Assert.Equal("unknown", Text(await GetAsync(http, id), "status"));
        Assert.Equal(("7490.25", Blocked), await BalanceAsync(http));

        await api.ReconcileAsync();

        var order = await GetAsync(http, id);
        Assert.Equal("accepted", Text(order, "status"));
        // Nawet gdy pierwsza proba weszla (504), na gieldzie jest jedno zlecenie — ponowienie mialo ten sam clientOrderId.
        Assert.Single(api.Parkiet.Placed, p => Cid(p) == Guid.Parse(id!).ToString("N"));
        Assert.Equal(("7490.25", Blocked), await BalanceAsync(http));
    }

    [Fact]
    public async Task Busy_after_a_timeout_does_not_give_the_money_back()
    {
        using var http = await FundedClientAsync();
        api.Parkiet.Then(Fault.Silence, Fault.Busy);
        var id = Text(await (await PlaceAsync(http, "o-1", Buy())).JsonAsync(), "id");

        await api.ReconcileAsync();

        Assert.Equal("unknown", Text(await GetAsync(http, id), "status"));
        Assert.Equal(("7490.25", Blocked), await BalanceAsync(http));
    }

    [Fact]
    public async Task Repeated_request_is_the_same_order_and_a_different_one_is_a_conflict()
    {
        using var http = await FundedClientAsync();
        var first = await PlaceAsync(http, "o-1", Buy());

        var again = await PlaceAsync(http, "o-1", Buy());
        var different = await PlaceAsync(http, "o-1", Buy(quantity: 11));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(Text(await first.JsonAsync(), "id"), Text(await again.JsonAsync(), "id"));
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal(("7490.25", Blocked), await BalanceAsync(http));
    }

    [Fact]
    public async Task Sell_blocks_shares_and_needs_them()
    {
        using var http = await FundedClientAsync();
        await api.Services.GetRequiredService<ILedgerStore>()
            .PostAsync($"shares-{Guid.NewGuid()}", JournalEntries.SharesTransferIn(Clients.ClientIdOf(http), "CDR", 5));

        var sell = await PlaceAsync(http, "s-1", new { symbol = "CDR", side = "sell", type = "market", quantity = 5 });
        var tooMany = await PlaceAsync(http, "s-2", new { symbol = "CDR", side = "sell", type = "limit", limitPrice = "300", quantity = 1 });

        Assert.Equal(HttpStatusCode.Created, sell.StatusCode);
        var sold = await sell.JsonAsync();
        Assert.Equal(("CDR", "5"), (Text(sold, "blockedAsset"), Text(sold, "blocked")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
    }

    private async Task<HttpClient> FundedClientAsync(string deposit = "10000")
    {
        var http = await Clients.RegisterAndLoginAsync(api);
        if (decimal.Parse(deposit, System.Globalization.CultureInfo.InvariantCulture) > 0)
        {
            (await http.PostMoneyAsync("/api/me/deposits", "dep", deposit)).EnsureSuccessStatusCode();
        }
        return http;
    }

    private static async Task<HttpResponseMessage> PlaceAsync(HttpClient http, string? key, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/orders") { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }
        return await http.SendAsync(request);
    }

    private static async Task<JsonElement> GetAsync(HttpClient http, string? id) =>
        await (await http.GetAsync(new Uri($"/api/me/orders/{id}", UriKind.Relative))).JsonAsync();

    private static async Task<(string? Available, string? Blocked)> BalanceAsync(HttpClient http)
    {
        var pln = (await (await http.GetAsync(new Uri("/api/me/balances", UriKind.Relative))).JsonAsync())
            .EnumerateArray().Single(b => b.GetProperty("asset").GetString() == "PLN");
        return (Text(pln, "available"), Text(pln, "blocked"));
    }

    private static string? Text(JsonElement json, string property) => json.GetProperty(property).GetString();

    private static string? Cid(JsonElement sent) => Text(sent, "clientOrderId");
}
