using System.Diagnostics.Metrics;

namespace BrokerLedger.Api;

/// <summary>
/// Metryki biznesowe brokera (System.Diagnostics.Metrics), wystawiane razem z metrykami HTTP na <c>/metrics</c>
/// w formacie Prometheusa: ile zlecen i z jakim skutkiem, ile zdarzen gieldy rozliczono, ile strumieni SSE jest otwartych.
/// </summary>
internal sealed class BrokerMetrics
{
    public const string MeterName = "BrokerLedger";

    private readonly Counter<long> _orders;
    private readonly Counter<long> _settled;
    private readonly UpDownCounter<long> _streams;

    public BrokerMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _orders = meter.CreateCounter<long>("broker.orders", "{order}", "Zlozone zlecenia wedlug strony i stanu po odpowiedzi gieldy");
        _settled = meter.CreateCounter<long>("broker.settled_events", "{event}", "Rozliczone zdarzenia gieldy ze skrzynki");
        _streams = meter.CreateUpDownCounter<long>("broker.event_streams", "{stream}", "Otwarte strumienie zdarzen klientow (SSE)");
    }

    public void OrderPlaced(string side, string status) =>
        _orders.Add(1, new KeyValuePair<string, object?>("side", side), new KeyValuePair<string, object?>("status", status));

    public void Settled(int events)
    {
        if (events > 0)
        {
            _settled.Add(events);
        }
    }

    public void StreamOpened() => _streams.Add(1);

    public void StreamClosed() => _streams.Add(-1);
}
