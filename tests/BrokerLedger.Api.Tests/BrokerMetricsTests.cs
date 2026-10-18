using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace BrokerLedger.Api.Tests;

/// <summary>Metryki biznesowe: nazwy i jednostki to kontrakt z Prometheusem i dashboardami, wiec sa sprawdzane wprost.</summary>
public class BrokerMetricsTests
{
    [Fact]
    public void Metrics_have_stable_names_and_count_what_happened()
    {
        using var services = new ServiceCollection().AddMetrics().AddSingleton<BrokerMetrics>().BuildServiceProvider();
        using var totals = new MetricTotals(services);
        var metrics = services.GetRequiredService<BrokerMetrics>();

        metrics.OrderPlaced("buy", "accepted");
        metrics.Settled(3);
        metrics.StreamOpened();
        metrics.StreamOpened();
        metrics.StreamClosed();

        Assert.Equal(
            [("broker.orders", "{order}"), ("broker.settled_events", "{event}"), ("broker.event_streams", "{stream}")],
            totals.Instruments.Select(i => (i.Name, i.Unit)));
        Assert.All(totals.Instruments, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));
        Assert.Equal((1L, 3L, 1L), (totals["broker.orders"], totals["broker.settled_events"], totals["broker.event_streams"]));
    }
}

/// <summary>
/// Sumy pomiarow metryk brokera z jednego kontenera uslug. Filtr po fabryce miernikow, bo testy w tym samym
/// procesie (np. host API) tworza wlasne liczniki o tych samych nazwach.
/// </summary>
internal sealed class MetricTotals : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, long> _totals = new();
    private readonly ConcurrentQueue<Instrument> _instruments = new();

    public MetricTotals(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == factory)
            {
                _instruments.Enqueue(instrument);
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            _totals.AddOrUpdate(instrument.Name, value, (_, total) => total + value));
        _listener.Start();
    }

    public IReadOnlyList<Instrument> Instruments => [.. _instruments];

    public long this[string name] => _totals.GetValueOrDefault(name);

    public void Dispose() => _listener.Dispose();
}
