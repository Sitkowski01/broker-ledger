using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using static BrokerLedger.Api.Tests.FakeParkiet;

namespace BrokerLedger.Api.Tests;

public sealed partial class OrderEndToEndTests
{
    [Fact]
    public async Task Client_sees_only_their_own_orders()
    {
        using var alice = await FundedClientAsync();
        using var bob = await FundedClientAsync();
        var id = Text(await (await PlaceAsync(alice, "o-1", Buy())).JsonAsync(), "id");
        await PlaceAsync(alice, "o-2", Buy(quantity: 1));

        Assert.Equal(2, (await (await alice.GetAsync(new Uri("/api/me/orders", UriKind.Relative))).JsonAsync()).GetArrayLength());
        Assert.Equal(1, (await (await alice.GetAsync(new Uri("/api/me/orders?limit=1", UriKind.Relative))).JsonAsync()).GetArrayLength());
        Assert.Equal(0, (await (await bob.GetAsync(new Uri("/api/me/orders", UriKind.Relative))).JsonAsync()).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(new Uri($"/api/me/orders/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(new Uri($"/api/me/orders/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(new Uri($"/api/me/orders/{Guid.Empty}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync(new Uri("/api/me/orders?limit=0", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Cancel_is_requested_on_the_exchange_and_the_money_waits_for_its_event()
    {
        using var http = await FundedClientAsync();
        var id = Text(await (await PlaceAsync(http, "o-1", Buy())).JsonAsync(), "id");

        var cancel = await http.DeleteAsync(new Uri($"/api/me/orders/{id}", UriKind.Relative));
        var again = await http.DeleteAsync(new Uri($"/api/me/orders/{id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal("cancelrequested", Text(await cancel.JsonAsync(), "status"));
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(("7490.25", Blocked), await BalanceAsync(http));
    }

    [Fact]
    public async Task Rejected_order_cannot_be_cancelled()
    {
        using var http = await FundedClientAsync(deposit: "1");
        var id = Text(await (await PlaceAsync(http, "o-1", Buy())).JsonAsync(), "id");

        var cancel = await http.DeleteAsync(new Uri($"/api/me/orders/{id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal("Order is not open", Text(await cancel.JsonAsync(), "title"));
    }

    [Fact]
    public async Task Repeating_an_unexplained_order_shows_it_without_sending_again()
    {
        using var http = await FundedClientAsync();
        api.Parkiet.Then(Fault.Silence);
        var first = await PlaceAsync(http, "o-1", Buy());
        var calls = api.Parkiet.PlaceCalls;

        var again = await PlaceAsync(http, "o-1", Buy());

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var order = await again.JsonAsync();
        Assert.Equal((Text(await first.JsonAsync(), "id"), "unknown"), (Text(order, "id"), Text(order, "status")));
        Assert.Equal(calls, api.Parkiet.PlaceCalls);
    }

    [Fact]
    public async Task Cancel_without_an_answer_asks_to_try_again()
    {
        using var http = await FundedClientAsync();
        var id = Text(await (await PlaceAsync(http, "o-1", Buy())).JsonAsync(), "id");
        api.Parkiet.FailNextCancel();

        var cancel = await http.DeleteAsync(new Uri($"/api/me/orders/{id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, cancel.StatusCode);
        Assert.Equal("Exchange did not answer, try again", Text(await cancel.JsonAsync(), "title"));
        Assert.Equal("accepted", Text(await GetAsync(http, id), "status"));
    }

    [Fact]
    public async Task Messages_tell_the_client_what_to_fix()
    {
        using var http = await FundedClientAsync();
        await PlaceAsync(http, "k", Buy());

        var conflict = await PlaceAsync(http, "k", Buy(quantity: 2));
        var missing = await http.GetAsync(new Uri($"/api/me/orders/{Guid.NewGuid()}", UriKind.Relative));
        var page = await http.GetAsync(new Uri("/api/me/orders?limit=101", UriKind.Relative));
        var bad = await PlaceAsync(http, "bad", new { symbol = "", side = "x", type = "y", limitPrice = "abc" });

        Assert.Equal("Idempotency-Key was already used for a different order", Text(await conflict.JsonAsync(), "title"));
        Assert.Equal("Order not found", Text(await missing.JsonAsync(), "title"));
        Assert.Equal("limit must be 1-100", (await page.JsonAsync()).GetProperty("errors").GetProperty("limit")[0].GetString());
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri("/api/me/orders?limit=100", UriKind.Relative))).StatusCode);
        var errors = (await bad.JsonAsync()).GetProperty("errors");
        Assert.Equal("symbol is required", errors.GetProperty("symbol")[0].GetString());
        Assert.Equal("side must be buy or sell", errors.GetProperty("side")[0].GetString());
        Assert.Equal("type must be limit or market", errors.GetProperty("type")[0].GetString());
        Assert.Equal("limitPrice must be a decimal string like \"250.40\"", errors.GetProperty("limitPrice")[0].GetString());
        Assert.Equal("quantity is required", errors.GetProperty("quantity")[0].GetString());
    }
}
