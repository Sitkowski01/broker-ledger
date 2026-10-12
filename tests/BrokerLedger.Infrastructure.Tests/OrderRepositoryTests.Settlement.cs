using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

public sealed partial class OrderRepositoryTests
{
    private static void AcceptAs(Order order, long exchangeId)
    {
        order.MarkFundsBlocked();
        order.Accept(exchangeId);
    }

    private static ExchangeEvent.Trade Fill(long sequence, long quantity, decimal price = 250m) =>
        new("CDR", new EventPosition(3, sequence), 99, 7, OrderSide.Buy, price, quantity);

    [Fact]
    public async Task Stores_and_reads_back_the_execution()
    {
        var order = await Saved(NewOrder(), o =>
        {
            AcceptAs(o, 7);
            o.ApplyTrade(Fill(12, 4, 250.40m), Commission.Standard);
        });

        var read = (await _orders.GetAsync(order.Id))!;

        Assert.Equal(new OrderExecution(4, 1001.60m, 3.91m, new EventPosition(3, 12)), read.Execution);
        Assert.Equal(OrderExecution.None, (await _orders.AddOrGetAsync(NewOrder("fresh"))).Execution);
    }

    [Fact]
    public async Task Finished_orders_with_blocks_left_need_attention()
    {
        var filled = await Saved(NewOrder("filled"), o => { AcceptAs(o, 7); o.ApplyTrade(Fill(1, 10, 200m), Commission.Standard); });
        var cancelled = await Saved(NewOrder("cancelled"), o =>
        {
            AcceptAs(o, 8);
            o.ApplyCancellation(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, 2), 8, 10, "REQUESTED"));
        });
        await Saved(NewOrder("released"), o =>
        {
            AcceptAs(o, 9);
            o.ApplyCancellation(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, 3), 9, 10, "REQUESTED"));
            o.MarkFundsReleased();
        });

        var due = await _orders.NeedingAttentionAsync(DateTimeOffset.MaxValue, 10);

        Assert.Equivalent(new[] { filled.Id, cancelled.Id }, due.Select(o => o.Id), strict: true);
    }

    [Fact]
    public async Task Finds_orders_by_exchange_number_within_a_symbol()
    {
        var first = await Saved(NewOrder("a"), o => AcceptAs(o, 7));
        _clock.Advance(TimeSpan.FromSeconds(1));
        var recycled = await Saved(NewOrder("b"), o => AcceptAs(o, 7));
        await Saved(NewOrder("c", Buy with { Symbol = "PKN" }), o => AcceptAs(o, 7));
        await Saved(NewOrder("d"), o => AcceptAs(o, 8));

        var found = await _orders.ByExchangeOrderIdAsync("CDR", 7);

        Assert.Equal([first.Id, recycled.Id], found.Select(o => o.Id));
        Assert.Empty(await _orders.ByExchangeOrderIdAsync("CDR", 6));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _orders.ByExchangeOrderIdAsync(" ", 7));
    }

    [Fact]
    public async Task Order_is_in_flight_once_it_may_have_reached_the_exchange()
    {
        await _orders.AddOrGetAsync(NewOrder("not sent"));
        await Saved(NewOrder("blocked"), o => o.MarkFundsBlocked());
        await Saved(NewOrder("accepted"), o => AcceptAs(o, 1));
        Assert.False(await _orders.HasInFlightAsync("CDR"));

        await Saved(NewOrder("sending", Buy with { Symbol = "PKN" }), o => { o.MarkFundsBlocked(); o.BeginSubmission(); });
        await Saved(NewOrder("unknown", Buy with { Symbol = "ALE" }), o => { o.MarkFundsBlocked(); o.BeginSubmission(); o.MarkUnknown(); });

        Assert.Equal((false, true, true), (await _orders.HasInFlightAsync("CDR"), await _orders.HasInFlightAsync("PKN"), await _orders.HasInFlightAsync("ALE")));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _orders.HasInFlightAsync(""));
    }

    [Fact]
    public async Task Database_refuses_impossible_executions()
    {
        var order = await Saved(NewOrder(), o => AcceptAs(o, 7));

        await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync($"UPDATE orders SET filled_quantity = 11 WHERE id = '{order.Id}'"));
        await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync($"UPDATE orders SET filled_quantity = -1 WHERE id = '{order.Id}'"));
        await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync($"UPDATE orders SET last_event_run_id = 1 WHERE id = '{order.Id}'"));
        Assert.Equal(1, await _db.ExecuteAsync($"UPDATE orders SET filled_quantity = 10 WHERE id = '{order.Id}'"));
    }
}
