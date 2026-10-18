using System.Net;

namespace BrokerLedger.Api.Tests;

/// <summary><c>GET /api/me/performance</c>: wynik rachunku bez wplat, policzony z ksiegi.</summary>
[Collection(SharedApi.Name)]
public sealed class PerformanceEndToEndTests(BrokerApi api)
{
    [Fact]
    public async Task A_deposit_is_value_but_not_result()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        await http.PostMoneyAsync("/api/me/deposits", "dep-1", "1000");
        await http.PostMoneyAsync("/api/me/withdrawals", "wd-1", "250.50");

        var points = (await (await http.GetAsync(new Uri("/api/me/performance?days=7", UriKind.Relative))).JsonAsync())
            .GetProperty("points").EnumerateArray().ToList();

        // Rachunek otwarty dzis: jeden punkt — "teraz".
        var now = Assert.Single(points);
        Assert.Equal(("749.50", "749.50", "0.00"), (
            now.GetProperty("value").GetString(), now.GetProperty("invested").GetString(), now.GetProperty("result").GetString()));
    }

    [Fact]
    public async Task New_account_has_no_points_and_only_week_or_month_is_allowed()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);

        var empty = await (await http.GetAsync(new Uri("/api/me/performance", UriKind.Relative))).JsonAsync();
        var odd = await http.GetAsync(new Uri("/api/me/performance?days=5", UriKind.Relative));

        Assert.Equal(0, empty.GetProperty("points").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, odd.StatusCode);
        Assert.Equal("days must be 7 or 30", (await odd.JsonAsync()).GetProperty("errors").GetProperty("days")[0].GetString());
    }
}
