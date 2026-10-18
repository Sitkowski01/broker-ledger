using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrokerLedger.Api.Tests;

/// <summary><c>GET /api/me/events</c> po HTTP: Server-Sent Events z sygnalami z wyzwalaczy bazy.</summary>
[Collection(SharedApi.Name)]
public sealed class EventStreamTests(BrokerApi api)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Stream_says_ready_and_then_tells_about_the_clients_own_changes()
    {
        using var http = await Clients.RegisterAndLoginAsync(api);
        using var other = await Clients.RegisterAndLoginAsync(api);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await http.GetAsync(new Uri("/api/me/events", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        // Proxy (nginx w obrazie frontu) nie moze zbierac sygnalow w paczki.
        Assert.Equal("no", Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        Assert.Equal(("ready", "{}"), await NextEventAsync(reader, cts.Token));

        // Cudza wplata nie trafia do strumienia; wlasna tak.
        await other.PostMoneyAsync("/api/me/deposits", "dep-x", "5");
        await http.PostMoneyAsync("/api/me/deposits", "dep-1", "1000");
        Assert.Equal(("ledger", "{}"), await NextEventAsync(reader, cts.Token));

        using var place = new HttpRequestMessage(HttpMethod.Post, "/api/me/orders")
        {
            Content = JsonContent.Create(new { symbol = "CDR", side = "buy", type = "limit", limitPrice = "250.00", quantity = 1 }),
        };
        place.Headers.Add("Idempotency-Key", "o-1");
        var order = await (await http.SendAsync(place, cts.Token)).JsonAsync();
        var seen = new List<(string Event, string Data)>();
        while (seen.All(e => !e.Data.Contains("accepted", StringComparison.Ordinal)))
        {
            seen.Add(await NextEventAsync(reader, cts.Token));
        }
        // Saga: zapis, blokada (dokument w ksiedze), przyjecie przez gielde.
        Assert.Contains(("ledger", "{}"), seen);
        var accepted = JsonDocument.Parse(seen[^1].Data).RootElement;
        Assert.Equal(("order", order.GetProperty("id").GetString(), "accepted"),
            (seen[^1].Event, accepted.GetProperty("orderId").GetString(), accepted.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Stream_ends_when_the_token_expires()
    {
        using var factory = api.With(("Jwt:Lifetime", "00:00:02"), ("Events:Heartbeat", "00:00:00.200"));
        using var http = await Clients.RegisterAndLoginAsync(factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await http.GetAsync(new Uri("/api/me/events", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        Assert.Equal(("ready", "{}"), await NextEventAsync(reader, cts.Token));
        // Pingi podtrzymuja polaczenie, ale po wygasnieciu tokenu serwer je zamyka — zwyczajnie, bez urwania w polowie.
        while (await reader.ReadLineAsync(cts.Token) is not null)
        {
        }
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task Quiet_stream_sends_a_ping()
    {
        using var factory = api.With(("Events:Heartbeat", "00:00:00.200"));
        using var http = await Clients.RegisterAndLoginAsync(factory);
        using var streams = new MetricTotals(factory.Services);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await http.GetAsync(new Uri("/api/me/events", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        Assert.Equal("ready", (await NextEventAsync(reader, cts.Token)).Event);
        Assert.Equal("ping", (await NextEventAsync(reader, cts.Token)).Event);
        // Otwarty strumien widac w metrykach (licznik w gore przy polaczeniu, w dol przy rozlaczeniu).
        var metrics = await http.GetStringAsync(new Uri("/metrics", UriKind.Relative), cts.Token);
        Assert.Matches("""broker_event_streams\{[^}]*\} [1-9]""", metrics);
        Assert.Equal(1, streams["broker.event_streams"]);

        reader.Dispose();
        response.Dispose();
        var deadline = DateTime.UtcNow + Wait;
        while (streams["broker.event_streams"] != 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "stream was never counted as closed");
            await Task.Delay(50, cts.Token);
        }
    }

    [Fact]
    public async Task Stream_needs_a_token()
    {
        using var anonymous = api.CreateClient();

        var response = await anonymous.GetAsync(new Uri("/api/me/events", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Jedno zdarzenie SSE: linie "event:" i "data:", zakonczone pusta linia.</summary>
    private static async Task<(string Event, string Data)> NextEventAsync(StreamReader reader, CancellationToken ct)
    {
        string? name = null, data = null;
        while (await reader.ReadLineAsync(ct).AsTask().WaitAsync(Wait, ct) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line["data: ".Length..];
            }
            else if (line.Length == 0 && name is not null)
            {
                return (name, data ?? "");
            }
        }
        throw new EndOfStreamException("event stream ended");
    }
}
