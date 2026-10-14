using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrokerLedger.Api.Tests;

/// <summary>
/// Gielda Parkiet w pamieci: ten sam kontrakt REST (z deduplikacja po clientOrderId), a do tego awarie
/// na zyczenie testu. Najwazniejsza: 504, chociaz zlecenie weszlo — dokladnie to, co saga musi wyjasnic.
/// </summary>
public sealed partial class FakeParkiet
{
    public enum Fault
    {
        /// <summary>503: kolejka pelna, zlecenie nie weszlo.</summary>
        Busy,

        /// <summary>400: gielda odrzucila tresc.</summary>
        BadRequest,

        /// <summary>Zlecenie weszlo, ale odpowiedz to 504 — broker nie wie, ze sie udalo.</summary>
        PlacedButTimedOut,

        /// <summary>Brak odpowiedzi w ogole (timeout HttpClienta).</summary>
        Silence,

        /// <summary>Blad sieci przed dotarciem do gieldy.</summary>
        NetworkError,
    }

    private readonly ConcurrentQueue<Fault> _faults = new();
    private int _silentCancels;

    /// <summary>Nastepne anulowanie skonczy sie bledem sieci.</summary>
    public void FailNextCancel() => Interlocked.Increment(ref _silentCancels);
    private readonly ConcurrentDictionary<string, long> _byClientOrderId = new();
    private readonly ConcurrentDictionary<(string Symbol, long Id), bool> _open = new();
    private long _nextId;

    /// <summary>Przebieg gieldy — jak runId Parkietu. Zdarzenia z Kafki w testach niosa ten sam.</summary>
    public long Run => Interlocked.Read(ref _run);
    private long _run = 1;

    /// <summary>
    /// Start gieldy bez wznowienia: nowy przebieg, puste arkusze, numeracja od 1 i bez pamieci clientOrderId.
    /// Zlecenia brokera z poprzedniego przebiegu zostaja u niego otwarte, dopoki nie zamknie ich rozliczenie.
    /// </summary>
    public void Restart()
    {
        _open.Clear();
        _byClientOrderId.Clear();
        Interlocked.Exchange(ref _nextId, 0);
        Interlocked.Increment(ref _run);
    }

    /// <summary>Kazde zlecenie, ktore naprawde weszlo do arkusza.</summary>
    public ConcurrentQueue<JsonElement> Placed { get; } = new();

    public int PlaceCalls => _placeCalls;
    private int _placeCalls;

    /// <summary>Numer, pod ktorym gielda przyjela zlecenie brokera — takze wtedy, gdy broker go jeszcze nie zna.</summary>
    public long ExchangeIdOf(Guid brokerOrderId) => _byClientOrderId[brokerOrderId.ToString("N")];

    public FakeParkiet Then(params Fault[] faults)
    {
        foreach (var fault in faults)
        {
            _faults.Enqueue(fault);
        }
        return this;
    }

    public HttpMessageHandler Handler() => new FakeHandler(this);

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post && PlacePath().Match(path) is { Success: true } place)
        {
            Interlocked.Increment(ref _placeCalls);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var symbol = place.Groups[1].Value;
            // Bez zaplanowanej awarii gielda dziala normalnie.
            Fault? fault = _faults.TryDequeue(out var next) ? next : null;
            switch (fault)
            {
                case Fault.Busy:
                    return Problem(HttpStatusCode.ServiceUnavailable, "CDR: queue full");
                case Fault.BadRequest:
                    return Problem(HttpStatusCode.BadRequest, "limit order requires a price");
                case Fault.PlacedButTimedOut:
                    PlaceOnce(symbol, body);
                    return Problem(HttpStatusCode.GatewayTimeout, "engine did not answer");
                case Fault.Silence:
                    throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
                case Fault.NetworkError:
                    throw new HttpRequestException("connection refused");
                default:
                    return Reply(symbol, body);
            }
        }
        if (request.Method == HttpMethod.Delete && CancelPath().Match(path) is { Success: true } cancel)
        {
            if (Interlocked.Decrement(ref _silentCancels) >= 0)
            {
                throw new HttpRequestException("connection reset");
            }
            Interlocked.Exchange(ref _silentCancels, 0);
            var key = (cancel.Groups[1].Value, long.Parse(cancel.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            // Jak Parkiet: numer z innego przebiegu to nie to zlecenie, nawet jesli taki numer jest w arkuszu.
            var run = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["runId"];
            if (run is not null && run != Run.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                return Problem(HttpStatusCode.NotFound, $"order {key.Item2} of run {run} is not in the book");
            }
            return _open.TryRemove(key, out _)
                ? Json(HttpStatusCode.OK, new { type = "CANCELLED", orderId = key.Item2, quantity = 1, reason = "REQUESTED" })
                : Problem(HttpStatusCode.NotFound, "order not in book");
        }
        return Problem(HttpStatusCode.NotFound, "no such endpoint");
    }

    private HttpResponseMessage Reply(string symbol, JsonElement body)
    {
        var cid = body.GetProperty("clientOrderId").GetString()!;
        if (_byClientOrderId.TryGetValue(cid, out var earlier))
        {
            return Json(HttpStatusCode.OK, new { orderId = earlier, runId = Run, clientOrderId = cid, repeated = true, events = Array.Empty<object>() });
        }
        var id = PlaceOnce(symbol, body);
        return Json(HttpStatusCode.Created, new { orderId = id, runId = Run, clientOrderId = cid, events = Array.Empty<object>() });
    }

    private long PlaceOnce(string symbol, JsonElement body)
    {
        var cid = body.GetProperty("clientOrderId").GetString()!;
        return _byClientOrderId.GetOrAdd(cid, _ =>
        {
            var id = Interlocked.Increment(ref _nextId);
            _open[(symbol, id)] = true;
            Placed.Enqueue(body);
            return id;
        });
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string detail) =>
        Json(status, new { status = (int)status, detail });

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body) };

    [GeneratedRegex("^/api/instruments/([A-Z0-9]+)/orders$")]
    private static partial Regex PlacePath();

    [GeneratedRegex("^/api/instruments/([A-Z0-9]+)/orders/([0-9]+)$")]
    private static partial Regex CancelPath();

    private sealed class FakeHandler(FakeParkiet parkiet) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            parkiet.HandleAsync(request, cancellationToken);
    }
}
