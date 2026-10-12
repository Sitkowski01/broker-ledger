using BrokerLedger.Domain;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Kafka;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Ta sama Kafka co w compose Parkietu, w Dockerze — jedna na wszystkie testy konsumenta.</summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka-native:4.2.1").Build();

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public Task InitializeAsync() => _kafka.StartAsync();

    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    /// <summary>Wysyla wiadomosci jak OutboxRelay Parkietu: klucz = spolka.</summary>
    public async Task PublishAsync(string topic, params (string Symbol, string Json)[] messages)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = BootstrapServers }).Build();
        foreach (var (symbol, json) in messages)
        {
            await producer.ProduceAsync(topic, new Message<string, string> { Key = symbol, Value = json });
        }
    }
}

[Collection(SharedPostgres.Name)]
public sealed class ExchangeEventConsumerTests(PostgresFixture postgres, KafkaFixture kafka) : IAsyncLifetime, IClassFixture<KafkaFixture>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private readonly string _topic = $"exchange.events.{Guid.NewGuid():N}";
    private TestDatabase _db = null!;
    private PostgresExchangeInbox _inbox = null!;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        _inbox = _db.Inbox();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ExchangeEventConsumer Consumer(string group = "broker-ledger") => new(
        new ExchangeEventsSettings(kafka.BootstrapServers, _topic, group, TimeSpan.FromMilliseconds(100)),
        _inbox, _db.DeadLetters(), NullLogger<ExchangeEventConsumer>.Instance);

    private static string Trade(long sequence, string symbol = "CDR") =>
        $$"""{"runId":1,"symbol":"{{symbol}}","sequence":{{sequence}},"type":"TRADE","makerOrderId":1,"takerOrderId":2,"takerSide":"BUY","price":10000,"quantity":1}""";

    private async Task<IReadOnlyList<ExchangeEvent>> WaitForAsync(int count)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var pending = await _inbox.PendingAsync(100);
            if (pending.Count >= count || DateTime.UtcNow > deadline)
            {
                return pending;
            }
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Stores_trades_and_cancellations_and_skips_the_rest()
    {
        await kafka.PublishAsync(_topic,
            ("CDR", Trade(1)),
            ("CDR", """{"runId":1,"symbol":"CDR","sequence":2,"type":"RESTED","orderId":3,"side":"SELL","price":100,"quantity":1}"""),
            ("CDR", "garbage"),
            ("PKN", """{"runId":1,"symbol":"PKN","sequence":1,"type":"CANCELLED","orderId":3,"quantity":1,"reason":"REQUESTED"}"""),
            ("CDR", Trade(1)));
        var consumer = Consumer();

        await consumer.StartAsync(CancellationToken.None);
        var stored = await WaitForAsync(2);
        await consumer.StopAsync(CancellationToken.None);

        Assert.Equal([("CDR", 1L), ("PKN", 1L)], stored.Select(e => (e.Symbol, e.Position.Sequence)));
    }

    [Fact]
    public async Task Restarted_consumer_continues_after_the_stored_offset()
    {
        await kafka.PublishAsync(_topic, ("CDR", Trade(1)));
        var first = Consumer();
        await first.StartAsync(CancellationToken.None);
        await WaitForAsync(1);
        await first.StopAsync(CancellationToken.None);
        // Gdyby offset nie zostal zapisany, drugi konsument przeczytalby zdarzenie jeszcze raz — skrzynka by je odrzucila,
        // ale tu sprawdzamy, ze w ogole do niego nie wraca: usuwamy je i patrzymy, czy wroci.
        await _db.ExecuteAsync("DELETE FROM exchange_inbox");
        await kafka.PublishAsync(_topic, ("CDR", Trade(2)));

        var second = Consumer();
        await second.StartAsync(CancellationToken.None);
        var stored = await WaitForAsync(1);
        await Task.Delay(500);
        await second.StopAsync(CancellationToken.None);

        Assert.Equal([2L], (await _inbox.PendingAsync(10)).Select(e => e.Position.Sequence));
        Assert.Single(stored);
    }

    [Theory]
    [InlineData(" ", "t", "g", 1)]
    [InlineData("k:9092", "", "g", 1)]
    [InlineData("k:9092", "t", " ", 1)]
    [InlineData("k:9092", "t", "g", 0)]
    public void Consumer_needs_complete_settings(string servers, string topic, string group, int retrySeconds) =>
        Assert.ThrowsAny<ArgumentException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddExchangeEventsConsumer(servers, topic, group, TimeSpan.FromSeconds(retrySeconds)));

    [Fact]
    public async Task Intake_tells_what_happened_to_each_message()
    {
        var consumer = Consumer();

        Assert.Equal(ExchangeEventConsumer.Intake.Stored, await consumer.StoreAsync(Trade(1)));
        Assert.Equal(ExchangeEventConsumer.Intake.Duplicate, await consumer.StoreAsync(Trade(1)));
        Assert.Equal(ExchangeEventConsumer.Intake.Ignored,
            await consumer.StoreAsync("""{"runId":1,"symbol":"CDR","sequence":5,"type":"RESTED"}"""));
        Assert.Equal(ExchangeEventConsumer.Intake.Malformed, await consumer.StoreAsync("{"));
        Assert.Equal(ExchangeEventConsumer.Intake.Malformed, await consumer.StoreAsync(null));
        // Zle wiadomosci nie przepadaja: tresc i powod leza w exchange_dead_letters.
        Assert.Equal(2, await _db.CountAsync("exchange_dead_letters"));
        Assert.Equal(1, await _db.CountAsync("exchange_dead_letters WHERE message = chr(123) AND error LIKE 'not an exchange event%'"));
    }

    [Fact]
    public async Task Database_outage_is_retried_until_the_event_is_stored()
    {
        var flaky = new FlakyInbox(_inbox, failures: 2);
        var consumer = new ExchangeEventConsumer(
            new ExchangeEventsSettings(kafka.BootstrapServers, _topic, "g", TimeSpan.FromMilliseconds(10)),
            flaky, _db.DeadLetters(), NullLogger<ExchangeEventConsumer>.Instance);

        var intake = await consumer.StoreWithRetryAsync(Trade(1), CancellationToken.None);

        Assert.Equal((ExchangeEventConsumer.Intake.Stored, 3), (intake, flaky.Attempts));
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExchangeEventConsumer(new ExchangeEventsSettings("x", "t", "g", TimeSpan.FromSeconds(1)),
                new FlakyInbox(_inbox, failures: 1), _db.DeadLetters(), NullLogger<ExchangeEventConsumer>.Instance).StoreWithRetryAsync(Trade(2), stopping.Token));
    }

    private sealed class FlakyInbox(IExchangeInbox inner, int failures) : IExchangeInbox
    {
        public int Attempts { get; private set; }

        public Task<bool> AddAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default) =>
            ++Attempts <= failures ? throw new TimeoutException("database did not answer") : inner.AddAsync(exchangeEvent, cancellationToken);

        public Task<IReadOnlyList<ExchangeEvent>> PendingAsync(int limit, CancellationToken cancellationToken = default) =>
            inner.PendingAsync(limit, cancellationToken);

        public Task MarkSettledAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default) =>
            inner.MarkSettledAsync(exchangeEvent, cancellationToken);
    }
}
