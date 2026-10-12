using BrokerLedger.Api.Orders;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BrokerLedger.Api.Tests;

/// <summary>Proces rozliczen bez serwera: ile bierze naraz i jak znosi bledy.</summary>
public class SettlementWorkerTests
{
    [Fact]
    public async Task Pass_settles_a_batch_from_the_inbox()
    {
        var inbox = new Inbox(Trade(1), Trade(2), Trade(3));

        var settled = await Worker(inbox, batch: 2).RunOnceAsync();

        // Nikt z brokera nie bral udzialu — zdarzenia "nie nasze" tez sa rozliczone (odhaczone).
        Assert.Equal((2, 2), (settled, inbox.LastLimit));
        Assert.Equal(2, inbox.Settled);
    }

    [Fact]
    public async Task Failed_event_is_counted_out_and_its_symbol_waits()
    {
        var inbox = new Inbox(Trade(1), Trade(2, "PKN"));
        var orders = new FailingOrders("CDR");

        var logger = new ListLogger();
        var worker = Worker(inbox, orders: orders, logger: logger);

        var settled = await worker.RunOnceAsync();
        await worker.RunOnceAsync();
        await worker.RunOnceAsync();

        Assert.Equal(1, settled);
        // Ten sam blad w kazdym przebiegu trafia do logu raz — nie cztery razy na sekunde.
        Assert.Contains("Settling CDR event 1:1 failed", Assert.Single(logger.Errors), StringComparison.Ordinal);
        orders.Error = "disk full";
        await worker.RunOnceAsync();
        Assert.Equal(2, logger.Errors.Count);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task Failed_pass_is_swallowed_while_the_app_runs(Type failure)
    {
        var inbox = new Inbox { FailWith = (Exception)Activator.CreateInstance(failure)! };

        await Worker(inbox).TickAsync(CancellationToken.None);

        Assert.Equal(1, inbox.Reads);
    }

    [Fact]
    public async Task Pass_cancelled_by_stopping_the_app_is_not_swallowed()
    {
        var inbox = new Inbox { FailWith = new OperationCanceledException() };
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Worker(inbox).TickAsync(stopping.Token));
    }

    [Fact]
    public async Task Keeps_running_after_failed_passes_and_stops_with_the_app()
    {
        var inbox = new Inbox { FailWith = new TimeoutException("database") };
        var worker = Worker(inbox, interval: TimeSpan.FromMilliseconds(10));

        await worker.StartAsync(CancellationToken.None);
        for (var i = 0; i < 200 && inbox.Reads < 3; i++)
        {
            await Task.Delay(10);
        }
        await worker.StopAsync(CancellationToken.None);

        Assert.True(inbox.Reads >= 3, $"reads: {inbox.Reads}");
        Assert.False(worker.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task Disabled_worker_does_nothing()
    {
        var inbox = new Inbox(Trade(1));
        var worker = Worker(inbox, enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        Assert.Equal(0, inbox.Reads);
    }

    [Fact]
    public void Kafka_defaults_match_parkiet()
    {
        var options = new ExchangeEventsOptions();

        Assert.Equal(("exchange.events", "broker-ledger", TimeSpan.FromSeconds(1)), (options.Topic, options.GroupId, options.RetryDelay));
        Assert.Null(options.BootstrapServers);
    }

    [Fact]
    public void Options_must_make_sense()
    {
        Assert.True(new SettlementOptions().IsValid());
        Assert.False(new SettlementOptions { Interval = TimeSpan.Zero }.IsValid());
        Assert.False(new SettlementOptions { BatchSize = 0 }.IsValid());
        Assert.Equal((TimeSpan.FromMilliseconds(250), 200, true),
            (new SettlementOptions().Interval, new SettlementOptions().BatchSize, new SettlementOptions().Enabled));
    }

    private static ExchangeEvent.Trade Trade(long sequence, string symbol = "CDR") =>
        new ExchangeEvent.Trade(symbol, new EventPosition(1, sequence), 1, 2, OrderSide.Buy, 10m, 1);

    private static SettlementWorker Worker(Inbox inbox, int batch = 50, bool enabled = true, TimeSpan? interval = null,
        IOrderRepository? orders = null, ILogger<SettlementWorker>? logger = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IExchangeInbox>(inbox)
            .AddSingleton(orders ?? new FailingOrders(null))
            .AddSingleton<ILedgerStore>(new NoLedger())
            .AddSingleton(Commission.Standard)
            .AddTransient<TradeSettlement>()
            .BuildServiceProvider();
        var options = Options.Create(new SettlementOptions { BatchSize = batch, Enabled = enabled, Interval = interval ?? TimeSpan.FromSeconds(1) });
        return new SettlementWorker(services.GetRequiredService<IServiceScopeFactory>(), options, TimeProvider.System,
            logger ?? NullLogger<SettlementWorker>.Instance);
    }

    private sealed class Inbox(params ExchangeEvent[] events) : IExchangeInbox
    {
        public int Reads { get; private set; }
        public int? LastLimit { get; private set; }
        public int Settled { get; private set; }
        public Exception? FailWith { get; init; }

        public Task<bool> AddAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExchangeEvent>> PendingAsync(int limit, CancellationToken cancellationToken = default)
        {
            Reads++;
            LastLimit = limit;
            return FailWith is not null
                ? Task.FromException<IReadOnlyList<ExchangeEvent>>(FailWith)
                : Task.FromResult<IReadOnlyList<ExchangeEvent>>(events.Take(limit).ToList());
        }

        public Task MarkSettledAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default)
        {
            Settled++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Zadnych zlecen brokera; dla <paramref name="brokenSymbol"/> baza "pada".</summary>
    private sealed class FailingOrders(string? brokenSymbol) : IOrderRepository
    {
        public string Error { get; set; } = "database";

        public Task<IReadOnlyList<Order>> ByExchangeOrderIdAsync(string symbol, long exchangeOrderId, CancellationToken cancellationToken = default) =>
            symbol == brokenSymbol
                ? Task.FromException<IReadOnlyList<Order>>(new TimeoutException(Error))
                : Task.FromResult<IReadOnlyList<Order>>([]);

        public Task<bool> HasInFlightAsync(string symbol, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<Order> AddOrGetAsync(Order order, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Order>> ClientOrdersAsync(ClientId client, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(Order order, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Order>> NeedingAttentionAsync(DateTimeOffset updatedBefore, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ListLogger : ILogger<SettlementWorker>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }

    private sealed class NoLedger : ILedgerStore
    {
        public Task<PostOutcome> PostAsync(string idempotencyKey, JournalEntry entry, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<decimal> BalanceAsync(AccountId account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
