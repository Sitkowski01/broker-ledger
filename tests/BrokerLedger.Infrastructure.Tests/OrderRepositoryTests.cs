using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

[Collection(SharedPostgres.Name)]
public sealed partial class OrderRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly OrderRequest Buy = new("CDR", OrderSide.Buy, OrderType.Limit, 250.40m, 10);
    private readonly ClientId _client = ClientId.New();
    private TestDatabase _db = null!;
    private PostgresOrderRepository _orders = null!;
    private readonly MovableClock _clock = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        _orders = _db.Orders(_clock);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private Order NewOrder(string key = "k-1", OrderRequest? request = null) =>
        Order.Place(OrderId.New(), _client, key, request ?? Buy, Commission.Standard);

    [Fact]
    public async Task Stores_and_reads_back_every_field()
    {
        var order = NewOrder();
        await _orders.AddOrGetAsync(order);
        order.MarkFundsBlocked();
        order.Reject("queue full");
        await _orders.SaveAsync(order);

        var read = (await _orders.GetAsync(order.Id))!;

        Assert.Equal((order.Id, _client, "k-1", Buy, order.BlockedAmount), (read.Id, read.Client, read.ClientKey, read.Request, read.BlockedAmount));
        Assert.Equal((OrderStatus.Rejected, "queue full", true, false, 1),
            (read.Status, read.RejectionReason, read.FundsBlocked, read.FundsReleased, read.Version));
        Assert.Null(await _orders.GetAsync(OrderId.New()));
    }

    [Fact]
    public async Task Same_client_key_returns_the_first_order()
    {
        var first = await _orders.AddOrGetAsync(NewOrder("same"));

        var again = await _orders.AddOrGetAsync(NewOrder("same", Buy with { Quantity = 99 }));
        var otherClient = await _orders.AddOrGetAsync(Order.Place(OrderId.New(), ClientId.New(), "same", Buy, Commission.Standard));

        Assert.Equal((first.Id, 10L), (again.Id, again.Quantity));
        Assert.NotEqual(first.Id, otherClient.Id);
        Assert.Equal(2, await _db.CountAsync("orders"));
    }

    [Fact]
    public async Task Parallel_repeats_of_one_key_store_one_order()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _orders.AddOrGetAsync(NewOrder("race"))));

        Assert.Single(results.Select(o => o.Id).Distinct());
        Assert.Equal(1, await _db.CountAsync("orders"));
    }

    [Fact]
    public async Task Stale_copy_cannot_overwrite_a_newer_save()
    {
        var order = await _orders.AddOrGetAsync(NewOrder());
        var stale = (await _orders.GetAsync(order.Id))!;
        order.MarkFundsBlocked();
        await _orders.SaveAsync(order);

        stale.Reject("busy");

        var ex = await Assert.ThrowsAsync<OrderConcurrencyException>(() => _orders.SaveAsync(stale));
        Assert.Equal(order.Id, ex.Order);
        Assert.Equal(OrderStatus.Pending, (await _orders.GetAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task Reconciler_sees_only_unfinished_orders_old_enough()
    {
        var pending = await _orders.AddOrGetAsync(NewOrder("pending"));
        var accepted = await Saved(NewOrder("accepted"), o => { o.MarkFundsBlocked(); o.Accept(1, 1); });
        var toRelease = await Saved(NewOrder("release"), o => { o.MarkFundsBlocked(); o.Reject("busy"); });
        await Saved(NewOrder("released"), o => { o.MarkFundsBlocked(); o.Reject("busy"); o.MarkFundsReleased(); });
        await Saved(NewOrder("no-funds"), o => o.Reject("insufficient funds"));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var fresh = await _orders.AddOrGetAsync(NewOrder("fresh"));

        var due = await _orders.NeedingAttentionAsync(_clock.GetUtcNow() - TimeSpan.FromSeconds(30), 10);

        Assert.Equivalent(new[] { pending.Id, toRelease.Id }, due.Select(o => o.Id), strict: true);
        Assert.DoesNotContain(due, o => o.Id == accepted.Id || o.Id == fresh.Id);
        Assert.Single(await _orders.NeedingAttentionAsync(DateTimeOffset.MaxValue, 1));
    }

    [Fact]
    public async Task Client_orders_are_newest_first_and_only_their_own()
    {
        var older = await _orders.AddOrGetAsync(NewOrder("a"));
        _clock.Advance(TimeSpan.FromSeconds(1));
        var newer = await _orders.AddOrGetAsync(NewOrder("b"));
        await _orders.AddOrGetAsync(Order.Place(OrderId.New(), ClientId.New(), "c", Buy, Commission.Standard));

        Assert.Equal([newer.Id, older.Id], (await _orders.ClientOrdersAsync(_client, 10)).Select(o => o.Id));
        Assert.Single(await _orders.ClientOrdersAsync(_client, 1));
    }

    [Fact]
    public async Task Rejects_missing_arguments()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _orders.AddOrGetAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _orders.GetAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _orders.SaveAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _orders.ClientOrdersAsync(null!, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _orders.ClientOrdersAsync(_client, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _orders.NeedingAttentionAsync(DateTimeOffset.MaxValue, 0));
    }

    private async Task<Order> Saved(Order order, Action<Order> change)
    {
        var stored = await _orders.AddOrGetAsync(order);
        change(stored);
        await _orders.SaveAsync(stored);
        // Jak saga: po udanym zapisie kopia w pamieci ma nowa wersje.
        stored.MarkSaved();
        return stored;
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Fact]
    public async Task Submit_attempts_are_stored_and_the_longest_untouched_order_comes_first()
    {
        var first = await Saved(NewOrder("a"), o => { o.MarkFundsBlocked(); o.BeginSubmission(); o.MarkUnknown(); });
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = await _orders.AddOrGetAsync(NewOrder("b"));
        _clock.Advance(TimeSpan.FromSeconds(1));
        first.BeginSubmission();
        await _orders.SaveAsync(first);

        var due = await _orders.NeedingAttentionAsync(DateTimeOffset.MaxValue, 10);

        Assert.Equal([second.Id, first.Id], due.Select(o => o.Id));
        Assert.Equal(2, due[1].SubmitAttempts);
    }

    [Fact]
    public async Task Orders_placed_in_the_same_instant_are_newest_id_first()
    {
        var first = await _orders.AddOrGetAsync(Order.Place(new OrderId(Guid.CreateVersion7(_clock.GetUtcNow())), _client, "a", Buy, Commission.Standard));
        var second = await _orders.AddOrGetAsync(Order.Place(new OrderId(Guid.CreateVersion7(_clock.GetUtcNow().AddMilliseconds(1))), _client, "b", Buy, Commission.Standard));

        Assert.Equal([second.Id, first.Id], (await _orders.ClientOrdersAsync(_client, 10)).Select(o => o.Id));
    }

    [Fact]
    public async Task Order_changed_exactly_at_the_cutoff_is_left_for_later()
    {
        await _orders.AddOrGetAsync(NewOrder());

        Assert.Empty(await _orders.NeedingAttentionAsync(_clock.GetUtcNow(), 10));
        Assert.Single(await _orders.NeedingAttentionAsync(_clock.GetUtcNow().AddMilliseconds(1), 10));
    }
}
