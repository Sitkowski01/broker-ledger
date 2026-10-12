using Confluent.Kafka;
using Microsoft.AspNetCore.Hosting;
using Testcontainers.Kafka;

namespace BrokerLedger.Api.Tests;

/// <summary>
/// <see cref="BrokerApi"/> z prawdziwa Kafka w Dockerze: konsument zdarzen i rozliczenie w tle sa wlaczone,
/// a test gra role gieldy — publikuje zdarzenia w formacie Parkietu na <c>exchange.events</c>.
/// </summary>
public sealed class KafkaBrokerApi : BrokerApi
{
    public const string Topic = "exchange.events";

    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka-native:4.2.1").Build();
    private long _sequence;

    public override async Task InitializeAsync()
    {
        await _kafka.StartAsync();
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _kafka.DisposeAsync();
    }

    /// <summary>Transakcja z gieldy: cena w groszach, jak w EventMessage Parkietu.</summary>
    public Task<long> TradeAsync(string symbol, long maker, long taker, string takerSide, long priceInGrosze, long quantity,
        long? repeat = null) =>
        PublishAsync(symbol, repeat, sequence => $$"""
            {"runId":1,"symbol":"{{symbol}}","sequence":{{sequence}},"timestamp":"2026-10-12T10:00:00Z","type":"TRADE",
             "makerOrderId":{{maker}},"takerOrderId":{{taker}},"takerSide":"{{takerSide}}","price":{{priceInGrosze}},"quantity":{{quantity}}}
            """);

    public Task<long> CancelledAsync(string symbol, long orderId, long quantity, string reason) =>
        PublishAsync(symbol, null, sequence => $$"""
            {"runId":1,"symbol":"{{symbol}}","sequence":{{sequence}},"type":"CANCELLED","orderId":{{orderId}},"quantity":{{quantity}},"reason":"{{reason}}"}
            """);

    /// <summary><paramref name="repeat"/>: numer juz wyslanego zdarzenia — Kafka dostarcza co najmniej raz.</summary>
    private async Task<long> PublishAsync(string symbol, long? repeat, Func<long, string> json)
    {
        // Numery rosna wspolnie dla wszystkich spolek — wystarczy, ze sa unikalne w obrebie spolki.
        var sequence = repeat ?? Interlocked.Increment(ref _sequence);
        await SendAsync(symbol, json(sequence));
        return sequence;
    }

    private async Task SendAsync(string symbol, string json)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build();
        await producer.ProduceAsync(Topic, new Message<string, string> { Key = symbol, Value = json });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ExchangeEvents:BootstrapServers", _kafka.GetBootstrapAddress());
        builder.UseSetting("ExchangeEvents:RetryDelay", "00:00:00.200");
        builder.UseSetting("Orders:Settlement:Enabled", "true");
        builder.UseSetting("Orders:Settlement:Interval", "00:00:00.050");
    }
}

[CollectionDefinition(Name)]
public sealed class SharedKafkaApi : ICollectionFixture<KafkaBrokerApi>
{
    public const string Name = "kafka-api";
}
