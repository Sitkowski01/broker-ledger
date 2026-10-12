using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public partial class OrderSagaTests
{
    [Fact]
    public async Task Crash_after_blocking_resumes_without_blocking_twice()
    {
        _orders.FailOnSave = 1;
        _exchange.Then(new ExchangeReply.Accepted(9));
        var placed = NewBuy();

        await Assert.ThrowsAsync<TimeoutException>(() => PlaceAsync(placed));
        var stored = (await _orders.NeedingAttentionAsync(DateTimeOffset.MaxValue, 10)).Single();
        Assert.False(stored.FundsBlocked);

        var resumed = await _saga.ProgressAsync(stored);

        Assert.Equal(OrderStatus.Accepted, resumed.Status);
        Assert.Equal(2509.75m, Blocked);
        Assert.Single(_exchange.Placed);
    }

    [Fact]
    public async Task Crash_before_release_is_finished_by_the_next_pass()
    {
        _exchange.Then(new ExchangeReply.Refused("bad request"));
        var placed = NewBuy();
        _ledger.FailOnce = placed.ReleaseKey;

        await Assert.ThrowsAsync<TimeoutException>(() => PlaceAsync(placed));
        var stored = (await _orders.NeedingAttentionAsync(DateTimeOffset.MaxValue, 10)).Single();
        Assert.Equal((OrderStatus.Rejected, true, false), (stored.Status, stored.FundsBlocked, stored.FundsReleased));
        Assert.Equal(2509.75m, Blocked);

        var finished = await _saga.ProgressAsync(stored);

        Assert.True(finished.FundsReleased);
        Assert.Equal(0m, Blocked);
        Assert.Empty(await _orders.NeedingAttentionAsync(DateTimeOffset.MaxValue, 10));
    }

    [Fact]
    public async Task Repeated_request_with_the_same_key_is_the_same_order()
    {
        _exchange.Then(new ExchangeReply.Accepted(1));
        var first = await PlaceAsync(NewBuy("same"));

        var again = await PlaceAsync(NewBuy("same"));

        Assert.Equal(first.Id, again.Id);
        Assert.Single(_exchange.Placed);
        Assert.Equal(2509.75m, Blocked);
    }

    [Fact]
    public async Task Stale_copy_cannot_overwrite_a_newer_state()
    {
        _exchange.Then(new ExchangeReply.NoAnswer("timeout")).Then(new ExchangeReply.Accepted(3));
        var order = await PlaceAsync(NewBuy());
        var stale = await _orders.GetAsync(order.Id);
        await _saga.ProgressAsync(order);

        await Assert.ThrowsAsync<OrderConcurrencyException>(() => _saga.ProgressAsync(stale!));
        // Nieaktualna kopia nie zdazyla nawet zapytac gieldy: zapis proby jest przed wyslaniem.
        Assert.Equal(2, _exchange.Placed.Count);
    }

    [Fact]
    public async Task Cancel_asks_the_exchange_and_waits_for_its_event()
    {
        _exchange.Then(new ExchangeReply.Accepted(11)).ThenCancel(CancelReply.Cancelled);
        var order = await PlaceAsync(NewBuy());

        Assert.Equal(CancelReply.Cancelled, await _saga.CancelAsync(order));

        Assert.Equal(OrderStatus.CancelRequested, (await _orders.GetAsync(order.Id))!.Status);
        Assert.Equal([11L], _exchange.Cancelled);
        // Ile zwolnic, powie dopiero zdarzenie anulowania z gieldy (czesc mogla sie zrealizowac).
        Assert.Equal(2509.75m, Blocked);
        Assert.Equal(CancelReply.Cancelled, await _saga.CancelAsync(order));
        Assert.Single(_exchange.Cancelled);
    }

    [Fact]
    public async Task Cancel_overtaken_by_settlement_still_succeeded()
    {
        _exchange.Then(new ExchangeReply.Accepted(11)).ThenCancel(CancelReply.Cancelled);
        var order = await PlaceAsync(NewBuy());
        // Zdarzenie anulowania z gieldy rozliczono, zanim saga zapisala prosbe — zapis przegrywa wyscig o wersje.
        _orders.ConflictOnSave = _orders.Saves + 1;

        Assert.Equal(CancelReply.Cancelled, await _saga.CancelAsync(order));
        Assert.Equal(OrderStatus.Accepted, (await _orders.GetAsync(order.Id))!.Status);
    }

    [Theory]
    [InlineData(CancelReply.NotOpen)]
    [InlineData(CancelReply.NoAnswer)]
    public async Task Cancel_that_did_not_happen_leaves_the_order_open(CancelReply reply)
    {
        _exchange.Then(new ExchangeReply.Accepted(11)).ThenCancel(reply);
        var order = await PlaceAsync(NewBuy());

        Assert.Equal(reply, await _saga.CancelAsync(order));

        Assert.Equal(OrderStatus.Accepted, (await _orders.GetAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task Order_not_on_the_exchange_cannot_be_cancelled_there()
    {
        _exchange.Then(new ExchangeReply.Refused("bad"));
        var order = await PlaceAsync(NewBuy());

        Assert.Equal(CancelReply.NotOpen, await _saga.CancelAsync(order));
        Assert.Empty(_exchange.Cancelled);
        await Assert.ThrowsAsync<ArgumentNullException>(() => _saga.CancelAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _saga.ProgressAsync(null!));
    }

    [Fact]
    public async Task Busy_after_an_interrupted_first_attempt_is_not_a_rejection()
    {
        // Pierwsza proba poszla, ale klient sie rozlaczyl (anulowanie zadania) — status zostal Pending.
        _exchange.ThenThrow(new OperationCanceledException()).Then(new ExchangeReply.Busy("queue full"));
        var order = Order.Place(OrderId.New(), _client, "k", new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 250m, 10), Commission.Standard);
        await Assert.ThrowsAsync<OperationCanceledException>(() => PlaceAsync(order));
        var stored = (await _orders.GetAsync(order.Id))!;
        Assert.Equal((OrderStatus.Pending, 1), (stored.Status, stored.SubmitAttempts));

        var retried = await _saga.ProgressAsync(stored);

        Assert.Equal((OrderStatus.Unknown, 2), (retried.Status, retried.SubmitAttempts));
        Assert.Equal(2509.75m, Blocked);
    }

    [Fact]
    public async Task Two_senders_at_once_send_once()
    {
        _exchange.Then(new ExchangeReply.NoAnswer("timeout")).Then(new ExchangeReply.Accepted(8));
        var unknown = await PlaceAsync(NewBuy());
        var requestCopy = (await _orders.GetAsync(unknown.Id))!;
        var reconcilerCopy = (await _orders.GetAsync(unknown.Id))!;

        await _saga.ProgressAsync(requestCopy);
        await Assert.ThrowsAsync<OrderConcurrencyException>(() => _saga.ProgressAsync(reconcilerCopy));

        Assert.Equal(2, _exchange.Placed.Count);
        Assert.Equal(OrderStatus.Accepted, (await _orders.GetAsync(unknown.Id))!.Status);
    }
}
