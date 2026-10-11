using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BrokerLedger.Api.Tests;

/// <summary>Granice walidacji (dokladnie na limicie i tuz za nim) i tresc komunikatow, ktore widzi klient.</summary>
[Collection(SharedApi.Name)]
public sealed class ValidationTests(BrokerApi api)
{
    [Theory]
    [InlineData(7, HttpStatusCode.BadRequest)]
    [InlineData(8, HttpStatusCode.Created)]
    [InlineData(128, HttpStatusCode.Created)]
    [InlineData(129, HttpStatusCode.BadRequest)]
    public async Task Password_length_limits_are_inclusive(int length, HttpStatusCode expected)
    {
        using var http = api.CreateClient();

        var response = await http.PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@example.com", password = new string('p', length) });

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            Assert.Equal("password must be 8-128 characters", Error(await response.JsonAsync(), "password"));
        }
    }

    [Fact]
    public async Task Bad_email_says_what_is_expected()
    {
        using var http = api.CreateClient();

        var response = await http.PostAsJsonAsync("/api/auth/register", new { email = "nope", password = "secret-password" });

        Assert.Equal("a valid e-mail address is required", Error(await response.JsonAsync(), "email"));
    }

    [Fact]
    public async Task Failed_login_has_a_fixed_title()
    {
        using var http = api.CreateClient();

        var response = await http.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "x" });

        Assert.Equal("Invalid e-mail or password", (await response.JsonAsync()).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("!")]
    [InlineData("~")]
    [InlineData("order-2026-10-11_A~1")]
    public async Task Any_printable_ascii_key_is_accepted(string key)
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var response = await http.PostMoneyAsync("/api/me/deposits", key, "1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("posted", (await response.JsonAsync()).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Field_errors_say_what_is_expected()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var noKey = await http.PostMoneyAsync("/api/me/deposits", null, "1");
        var badAmount = await http.PostMoneyAsync("/api/me/deposits", "k", "abc");
        var badCurrency = await http.PostMoneyAsync("/api/me/deposits", "k", "1", "EUR");
        var fraction = await http.PostMoneyAsync("/api/me/deposits", "k", "0.001");

        Assert.Equal("header Idempotency-Key with 1-100 printable ASCII characters is required", Error(await noKey.JsonAsync(), "Idempotency-Key"));
        Assert.Equal("amount must be a positive decimal string like \"250.40\", at most 1000000000000", Error(await badAmount.JsonAsync(), "amount"));
        Assert.Equal("supported currencies: PLN", Error(await badCurrency.JsonAsync(), "currency"));
        Assert.Contains("fractions of a grosz", Error(await fraction.JsonAsync(), "amount"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1000000000000", HttpStatusCode.Created)]
    [InlineData("1000000000000.01", HttpStatusCode.BadRequest)]
    public async Task Amount_limit_is_inclusive(string amount, HttpStatusCode expected)
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var response = await http.PostMoneyAsync("/api/me/deposits", "big", amount);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task History_page_size_limits_are_inclusive(int limit)
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var response = await http.GetAsync(new Uri($"/api/me/history?limit={limit}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Too_large_page_says_the_limit()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var response = await http.GetAsync(new Uri("/api/me/history?limit=101", UriKind.Relative));

        Assert.Equal("limit must be 1-100", Error(await response.JsonAsync(), "limit"));
    }

    [Fact]
    public async Task History_shows_blocked_money_and_signs_by_direction()
    {
        // Blokad jeszcze nie ma w API (przyjda ze zleceniami) — ksiegujemy przez port domeny.
        using var http = await Clients.RegisterAndLoginAsync(api);
        await http.PostMoneyAsync("/api/me/deposits", "dep", "100");
        var client = await ClientIdAsync(http);
        await api.Services.GetRequiredService<ILedgerStore>()
            .PostAsync($"test-block-{client}", JournalEntries.Block(client, Asset.Cash(Currency.Pln), 30m));

        var history = await (await http.GetAsync(new Uri("/api/me/history", UriKind.Relative))).JsonAsync();
        var balance = (await (await http.GetAsync(new Uri("/api/me/balances", UriKind.Relative))).JsonAsync())[0];

        var block = history.GetProperty("items")[0].GetProperty("changes").EnumerateArray()
            .Select(c => (c.GetProperty("account").GetString(), c.GetProperty("change").GetString()));
        Assert.Equal([("available", "-30.00"), ("blocked", "+30.00")], block);
        Assert.Equal(("70.00", "30.00"), (balance.GetProperty("available").GetString(), balance.GetProperty("blocked").GetString()));
    }

    private static async Task<ClientId> ClientIdAsync(HttpClient http)
    {
        var token = http.DefaultRequestHeaders.Authorization!.Parameter!;
        var sub = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token).Subject;
        return await Task.FromResult(new ClientId(Guid.Parse(sub)));
    }

    private static string? Error(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field)[0].GetString();
}
