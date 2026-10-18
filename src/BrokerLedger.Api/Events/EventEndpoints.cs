using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using BrokerLedger.Api.Auth;
using BrokerLedger.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace BrokerLedger.Api.Events;

/// <summary>Sekcja <c>Events</c>: co ile pusty sygnal, zeby proxy nie zamknelo cichego polaczenia.</summary>
internal sealed class EventsOptions
{
    public const string Section = "Events";

    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// <c>GET /api/me/events</c> — Server-Sent Events: "u ciebie cos sie zmienilo" (zlecenie, ksiega, resync).
/// Bez kwot: front po sygnale pyta API o aktualny stan, wiec zgubiony sygnal niczego nie psuje.
/// </summary>
internal static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/events", Stream).RequireAuthorization().RequireRateLimiting(RateLimits.Client);
        return app;
    }

    private static ServerSentEventsResult<object> Stream(HttpContext http, IClientEvents events, IOptions<EventsOptions> options,
        BrokerMetrics metrics, TimeProvider clock, CancellationToken ct)
    {
        // Bez buforowania po drodze (np. nginx), inaczej sygnaly przychodzilyby paczkami.
        http.Response.Headers["X-Accel-Buffering"] = "no";
        // Token sprawdzany jest tylko przy otwarciu, a strumien z pingami zylby bez konca. Konczymy go razem z tokenem:
        // front polaczy sie od nowa z aktualnym tokenem (albo wcale, jesli sesja sie skonczyla).
        var expiry = new CancellationTokenSource(Until(http.User.ExpiresAt(), clock), clock);
        var stream = CancellationTokenSource.CreateLinkedTokenSource(ct, expiry.Token);
        http.Response.RegisterForDispose(expiry);
        http.Response.RegisterForDispose(stream);
        var items = ItemsAsync(events.SubscribeAsync(http.User.ClientId(), stream.Token), options.Value.Heartbeat, stream.Token);
        return TypedResults.ServerSentEvents(CountedAsync(EndWhenExpiredAsync(items, expiry.Token, ct), metrics, ct));
    }

    private static TimeSpan Until(DateTimeOffset moment, TimeProvider clock)
    {
        var left = moment - clock.GetUtcNow();
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>Wygasniecie tokenu to zwykly koniec strumienia, a nie blad w polowie odpowiedzi.</summary>
    internal static async IAsyncEnumerable<SseItem<object>> EndWhenExpiredAsync(
        IAsyncEnumerable<SseItem<object>> items, CancellationToken expired, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var enumerator = items.GetAsyncEnumerator(ct);
        while (true)
        {
            bool more;
            try
            {
                more = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException) when (expired.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                more = false;
            }
            if (!more)
            {
                yield break;
            }
            yield return enumerator.Current;
        }
    }

    /// <summary>Strumien liczony w metryce otwartych strumieni — od pierwszego zdarzenia do rozlaczenia.</summary>
    private static async IAsyncEnumerable<SseItem<object>> CountedAsync(
        IAsyncEnumerable<SseItem<object>> items, BrokerMetrics metrics, [EnumeratorCancellation] CancellationToken ct)
    {
        metrics.StreamOpened();
        try
        {
            await foreach (var item in items.WithCancellation(ct))
            {
                yield return item;
            }
        }
        finally
        {
            metrics.StreamClosed();
        }
    }

    /// <summary>Najpierw "ready" (klient wie, ze nasluchuje), potem zdarzenia, a w ciszy co jakis czas "ping".</summary>
    internal static async IAsyncEnumerable<SseItem<object>> ItemsAsync(
        IAsyncEnumerable<ClientEvent> source, TimeSpan heartbeat, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var events = source.GetAsyncEnumerator(ct);
        // Subskrypcja powstaje przy pierwszym MoveNextAsync, wiec "ready" dopiero po nim. Inaczej zmiana miedzy
        // odczytem ekranu a zapisaniem sie na sygnaly przepadlaby, a klient uwazalby sie za "na zywo".
        var next = events.MoveNextAsync().AsTask();
        try
        {
            yield return new SseItem<object>(new { }, "ready");
            while (true)
            {
                var winner = await Task.WhenAny(next, Task.Delay(heartbeat, ct));
                if (winner != next)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return new SseItem<object>(new { }, "ping");
                    continue;
                }
                if (!await next)
                {
                    yield break;
                }
                yield return ToItem(events.Current);
                next = events.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            // Rozlaczenie w trakcie czekania: enumeratora nie wolno zwolnic, dopoki MoveNextAsync nie skonczy.
            await ((Task)next).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static SseItem<object> ToItem(ClientEvent e) => e.Kind switch
    {
        ClientEventKind.Order => new SseItem<object>(
            new { orderId = e.Order!.Value, status = e.Status!.Value.ToString().ToLowerInvariant() }, "order"),
        ClientEventKind.Ledger => new SseItem<object>(new { }, "ledger"),
        _ => new SseItem<object>(new { }, "resync"),
    };
}
