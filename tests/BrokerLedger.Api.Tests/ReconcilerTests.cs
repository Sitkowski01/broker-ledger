using BrokerLedger.Api.Orders;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BrokerLedger.Api.Tests;

/// <summary>Reconciler bez serwera: ktore zlecenia bierze i jak znosi bledy pojedynczych zlecen.</summary>
public partial class ReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_broken_order_does_not_stop_the_others()
    {
        var orders = new Orders(Unknown(), Unknown(), Unknown());
        orders.SaveFailures.Enqueue(new OrderConcurrencyException(orders.All[0].Id));
        orders.SaveFailures.Enqueue(new TimeoutException("database"));
        var exchange = new Exchange();

        var progressed = await Reconciler(orders, exchange).RunOnceAsync();

        Assert.Equal(1, progressed);
        // Oba zepsute padly na zapisie proby — jeszcze przed wyslaniem na gielde.
        Assert.Equal(1, exchange.Calls);
    }

    [Fact]
    public async Task Takes_only_orders_older_than_min_age_in_batches()
    {
        var orders = new Orders();

        await Reconciler(orders, new Exchange(), minAge: TimeSpan.FromSeconds(15), batch: 7).RunOnceAsync();

        Assert.Equal((Now - TimeSpan.FromSeconds(15), 7), orders.LastQuery);
    }

    [Fact]
    public async Task Stopping_the_app_stops_the_pass()
    {
        var orders = new Orders(Unknown());
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reconciler(orders, new Exchange { ThrowCancelled = true }).RunOnceAsync(stopping.Token));
    }

    [Fact]
    public async Task Disabled_reconciler_does_nothing()
    {
        var orders = new Orders(Unknown());
        var reconciler = Reconciler(orders, new Exchange(), enabled: false);

        await reconciler.StartAsync(CancellationToken.None);
        await reconciler.ExecuteTask!;

        Assert.Null(orders.LastQuery);
    }

    [Fact]
    public void Options_must_make_sense()
    {
        Assert.True(new ReconcilerOptions().IsValid());
        Assert.False(new ReconcilerOptions { Interval = TimeSpan.Zero }.IsValid());
        Assert.False(new ReconcilerOptions { MinAge = TimeSpan.FromSeconds(-1) }.IsValid());
        Assert.True(new ReconcilerOptions { MinAge = TimeSpan.Zero }.IsValid());
        Assert.False(new ReconcilerOptions { BatchSize = 0 }.IsValid());
        Assert.Equal((TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), 50, true),
            (new ReconcilerOptions().Interval, new ReconcilerOptions().MinAge, new ReconcilerOptions().BatchSize, new ReconcilerOptions().Enabled));
    }

    [Fact]
    public async Task Order_that_stays_unknown_is_reported_every_few_attempts()
    {
        var orders = new Orders(Unknown());
        var logger = new Warnings();
        var reconciler = Reconciler(orders, new Exchange { Silent = true }, logger: logger);

        for (var pass = 1; pass <= 2 * OrderReconciler.StuckWarningEvery; pass++)
        {
            await reconciler.RunOnceAsync();
        }

        Assert.Equal(2, logger.Count);
        Assert.Equal(OrderStatus.Unknown, orders.All[0].Status);
    }

    private static OrderReconciler Reconciler(Orders orders, Exchange exchange, TimeSpan? minAge = null, int batch = 50, bool enabled = true,
        ILogger<OrderReconciler>? logger = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IOrderRepository>(orders)
            .AddSingleton<ILedgerStore>(new NoLedger())
            .AddSingleton<IExchange>(exchange)
            .AddTransient<OrderSaga>()
            .BuildServiceProvider();
        var options = Options.Create(new ReconcilerOptions { MinAge = minAge ?? TimeSpan.Zero, BatchSize = batch, Enabled = enabled });
        return new OrderReconciler(services.GetRequiredService<IServiceScopeFactory>(), options, new FixedClock(Now),
            logger ?? NullLogger<OrderReconciler>.Instance);
    }

    /// <summary>Liczy ostrzezenia — reszta logu nie ma tu znaczenia.</summary>
    private sealed class Warnings : ILogger<OrderReconciler>
    {
        public int Count { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Count += logLevel == LogLevel.Warning ? 1 : 0;
    }

    private static Order Unknown()
    {
        var order = Order.Place(OrderId.New(), ClientId.New(), "k", new OrderRequest("CDR", OrderSide.Sell, OrderType.Market, null, 1), Commission.Standard);
        order.MarkFundsBlocked();
        order.MarkUnknown();
        return order;
    }

    [Fact]
    public async Task Failing_pass_does_not_stop_the_reconciler()
    {
        // Wyjatek z BackgroundService zatrzymalby cala aplikacje — przebieg z bledem ma tylko zostac zalogowany.
        var orders = new Orders { FailQueries = 2 };
        var services = new ServiceCollection()
            .AddSingleton<IOrderRepository>(orders).AddSingleton<ILedgerStore>(new NoLedger())
            .AddSingleton<IExchange>(new Exchange()).AddTransient<OrderSaga>().BuildServiceProvider();
        var reconciler = new OrderReconciler(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReconcilerOptions { Interval = TimeSpan.FromMilliseconds(10), MinAge = TimeSpan.Zero }),
            TimeProvider.System, NullLogger<OrderReconciler>.Instance);

        await reconciler.StartAsync(CancellationToken.None);
        for (var i = 0; i < 200 && orders.Queries < 4; i++)
        {
            await Task.Delay(10);
        }
        await reconciler.StopAsync(CancellationToken.None);

        Assert.True(orders.Queries >= 4, $"queries: {orders.Queries}");
        Assert.False(reconciler.ExecuteTask!.IsFaulted);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task Failed_pass_is_swallowed_while_the_app_runs(Type failure)
    {
        // Takze OperationCanceledException, ktory nie pochodzi od zamykania aplikacji (np. timeout polaczenia).
        var orders = new Orders { FailWith = (Exception)Activator.CreateInstance(failure)! };

        await Reconciler(orders, new Exchange()).TickAsync(CancellationToken.None);

        Assert.Equal(1, orders.Queries);
    }

    [Fact]
    public async Task Pass_cancelled_by_stopping_the_app_is_not_swallowed()
    {
        var orders = new Orders { FailWith = new OperationCanceledException() };
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Reconciler(orders, new Exchange()).TickAsync(stopping.Token));
    }
}
