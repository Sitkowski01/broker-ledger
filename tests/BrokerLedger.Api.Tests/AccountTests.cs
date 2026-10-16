using System.Net;
using System.Text.Json;

namespace BrokerLedger.Api.Tests;

/// <summary>Cala droga: HTTP → uwierzytelnienie → ksiega → PostgreSQL → odpowiedz.</summary>
[Collection(SharedApi.Name)]
public sealed class AccountTests(BrokerApi api)
{
    [Fact]
    public async Task Deposit_withdraw_and_see_it_in_balances_and_history()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        Assert.Equal(HttpStatusCode.Created, (await http.PostMoneyAsync("/api/me/deposits", "dep-1", "250.40")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await http.PostMoneyAsync("/api/me/withdrawals", "wd-1", "50.4")).StatusCode);

        var balance = Assert.Single((await Get(http, "/api/me/balances")).EnumerateArray());
        Assert.Equal(("PLN", "Cash", "200.00", "0.00"), (
            balance.GetProperty("asset").GetString(), balance.GetProperty("kind").GetString(),
            balance.GetProperty("available").GetString(), balance.GetProperty("blocked").GetString()));

        var history = (await Get(http, "/api/me/history")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["withdrawal", "deposit"], history.Select(e => e.GetProperty("description").GetString()));
        var change = Assert.Single(history[0].GetProperty("changes").EnumerateArray());
        Assert.Equal(("available", "PLN", "-50.40"), (
            change.GetProperty("account").GetString(), change.GetProperty("asset").GetString(), change.GetProperty("change").GetString()));
        Assert.Equal("+250.40", history[1].GetProperty("changes")[0].GetProperty("change").GetString());

        // Pelny dokument wplaty: bank brokera na Wn, konto klienta na Ma; wplata nie dotyczy zadnego zlecenia.
        Assert.Equal(
            [("broker", "bank", "PLN", "debit", "250.40"), ("client", "available", "PLN", "credit", "250.40")],
            history[1].GetProperty("postings").EnumerateArray().Select(p => (
                p.GetProperty("owner").GetString(), p.GetProperty("account").GetString(), p.GetProperty("asset").GetString(),
                p.GetProperty("side").GetString(), p.GetProperty("amount").GetString())));
        Assert.Equal(JsonValueKind.Null, history[1].GetProperty("orderId").ValueKind);
    }

    [Fact]
    public async Task History_filters_by_kind_and_shows_the_key_as_the_client_sent_it()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        await http.PostMoneyAsync("/api/me/deposits", "dep-1", "100");
        await http.PostMoneyAsync("/api/me/withdrawals", "wd-1", "10");

        var transfers = (await Get(http, "/api/me/history?kind=transfers")).GetProperty("items").EnumerateArray().ToList();
        var trades = (await Get(http, "/api/me/history?kind=trades")).GetProperty("items");

        // Klucz bez przedrostka z id klienta — taki, jaki klient wyslal w Idempotency-Key.
        Assert.Equal(["wd-1", "dep-1"], transfers.Select(e => e.GetProperty("key").GetString()));
        Assert.Equal(0, trades.GetArrayLength());
    }

    [Fact]
    public async Task Retried_request_is_booked_once()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var first = await http.PostMoneyAsync("/api/me/deposits", "transfer-77", "100");
        var retry = await http.PostMoneyAsync("/api/me/deposits", "transfer-77", "100.00");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("alreadyPosted", (await retry.JsonAsync()).GetProperty("outcome").GetString());
        Assert.Equal("100.00", (await Get(http, "/api/me/balances"))[0].GetProperty("available").GetString());
    }

    [Fact]
    public async Task Reusing_a_key_for_something_else_is_a_conflict()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        await http.PostMoneyAsync("/api/me/deposits", "k-1", "100");

        var otherAmount = await http.PostMoneyAsync("/api/me/deposits", "k-1", "100.01");
        var otherOperation = await http.PostMoneyAsync("/api/me/withdrawals", "k-1", "100");

        Assert.Equal(HttpStatusCode.Conflict, otherAmount.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherOperation.StatusCode);
        Assert.Equal("100.00", (await Get(http, "/api/me/balances"))[0].GetProperty("available").GetString());
    }

    [Fact]
    public async Task Two_clients_may_use_the_same_key()
    {
        using var alice = await Clients.RegisterAndLoginAsync(api);
        using var bob = await Clients.RegisterAndLoginAsync(api);

        Assert.Equal(HttpStatusCode.Created, (await alice.PostMoneyAsync("/api/me/deposits", "1", "10")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await bob.PostMoneyAsync("/api/me/deposits", "1", "20")).StatusCode);

        Assert.Equal("10.00", (await Get(alice, "/api/me/balances"))[0].GetProperty("available").GetString());
        Assert.Equal("20.00", (await Get(bob, "/api/me/balances"))[0].GetProperty("available").GetString());
        Assert.Single((await Get(bob, "/api/me/history")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Withdrawing_too_much_says_how_much_is_available()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        await http.PostMoneyAsync("/api/me/deposits", "dep", "99.99");

        var response = await http.PostMoneyAsync("/api/me/withdrawals", "wd", "100");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.JsonAsync();
        Assert.Equal(("Insufficient funds", "PLN", "99.99"), (
            problem.GetProperty("title").GetString(), problem.GetProperty("asset").GetString(), problem.GetProperty("available").GetString()));
    }

    [Theory]
    [InlineData(null, "10", "PLN", "Idempotency-Key")]
    [InlineData("", "10", "PLN", "Idempotency-Key")]
    [InlineData("has space", "10", "PLN", "Idempotency-Key")]
    [InlineData("k", "1e3", "PLN", "amount")]
    [InlineData("k", "-5", "PLN", "amount")]
    [InlineData("k", "0", "PLN", "amount")]
    [InlineData("k", "1,000.00", "PLN", "amount")]
    [InlineData("k", "0.001", "PLN", "amount")]
    [InlineData("k", "10", "pln", "currency")]
    [InlineData("k", "10", "", "currency")]
    [InlineData("k", "10", "USD", "currency")]
    public async Task Invalid_requests_get_400_with_the_field(string? key, string amount, string currency, string field)
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var response = await http.PostMoneyAsync("/api/me/deposits", key, amount, currency);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Shortfall_reaches_a_client_that_does_not_accept_json()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/withdrawals")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { amount = "1", currency = "PLN" }),
        };
        request.Headers.Add("Idempotency-Key", "wd");
        request.Headers.Accept.ParseAdd("text/plain");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Insufficient funds", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Key_longer_than_100_characters_is_rejected()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        Assert.Equal(HttpStatusCode.Created, (await http.PostMoneyAsync("/api/me/deposits", new string('k', 100), "1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostMoneyAsync("/api/me/deposits", new string('k', 101), "1")).StatusCode);
    }

    [Fact]
    public async Task History_pages_with_a_cursor()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        foreach (var i in Enumerable.Range(1, 3))
        {
            await http.PostMoneyAsync("/api/me/deposits", $"dep-{i}", $"{i}");
        }

        var first = await Get(http, "/api/me/history?limit=2");
        var cursor = first.GetProperty("nextBefore").GetInt64();
        var second = await Get(http, $"/api/me/history?limit=2&before={cursor}");

        Assert.Equal(["+3.00", "+2.00"], Changes(first));
        Assert.Equal(["+1.00"], Changes(second));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task History_page_size_is_limited(int limit)
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var response = await http.GetAsync(new Uri($"/api/me/history?limit={limit}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Parallel_withdrawals_over_http_never_overdraw()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        await http.PostMoneyAsync("/api/me/deposits", "dep", "50");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => http.PostMoneyAsync("/api/me/withdrawals", $"wd-{i}", "10")));

        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal("0.00", (await Get(http, "/api/me/balances"))[0].GetProperty("available").GetString());
    }

    private static async Task<JsonElement> Get(HttpClient http, string path)
    {
        var response = await http.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.JsonAsync();
    }

    private static List<string?> Changes(JsonElement page) =>
        page.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("changes")[0].GetProperty("change").GetString()).ToList();
}
