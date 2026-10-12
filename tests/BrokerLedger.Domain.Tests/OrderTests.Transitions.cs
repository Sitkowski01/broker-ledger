using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public partial class OrderTests
{
    [Fact]
    public void Happy_path_blocks_then_gets_accepted()
    {
        var order = Place();

        order.MarkFundsBlocked();
        order.Accept(42);
        order.Accept(42);

        Assert.Equal((OrderStatus.Accepted, 42L), (order.Status, order.ExchangeOrderId));
        Assert.False(order.NeedsAttention);
    }

    [Fact]
    public void Unknown_answer_can_still_end_in_acceptance_or_rejection()
    {
        var accepted = Place();
        accepted.MarkFundsBlocked();
        accepted.MarkUnknown();
        accepted.MarkUnknown();
        Assert.True(accepted.NeedsAttention);
        accepted.Accept(7);

        var rejected = Place();
        rejected.MarkFundsBlocked();
        rejected.MarkUnknown();
        rejected.Reject("unknown instrument");

        Assert.Equal(OrderStatus.Accepted, accepted.Status);
        Assert.Equal(("unknown instrument", true), (rejected.RejectionReason, rejected.NeedsAttention));
    }

    [Fact]
    public void Rejected_order_releases_its_block_once()
    {
        var order = Place();
        order.MarkFundsBlocked();
        order.Reject("busy");

        Assert.Equal(JournalEntries.Release(Client, Asset.Cash(Currency.Pln), order.BlockedAmount).Postings, order.ReleaseEntry()!.Postings);
        order.MarkFundsReleased();

        Assert.False(order.NeedsAttention);
        Assert.Throws<InvalidOrderTransitionException>(order.ReleaseEntry);
    }

    [Fact]
    public void Rejection_without_a_block_needs_nothing_more()
    {
        var order = Place();
        order.Reject(OrderSaga.InsufficientFunds);

        Assert.False(order.NeedsAttention);
        Assert.Throws<InvalidOrderTransitionException>(order.ReleaseEntry);
        Assert.Throws<InvalidOrderTransitionException>(order.MarkFundsReleased);
    }

    [Fact]
    public void Illegal_moves_name_the_order_status_and_action()
    {
        var order = Place();

        var ex = Assert.Throws<InvalidOrderTransitionException>(() => order.Accept(1));

        Assert.Equal($"order {order.Id} in status Pending cannot accept", ex.Message);
        Assert.Equal(OrderStatus.Pending, ex.Status);
        Assert.Throws<InvalidOrderTransitionException>(order.RequestCancel);
        order.MarkFundsBlocked();
        Assert.Throws<ArgumentOutOfRangeException>(() => order.Accept(0));
        order.Accept(3);
        Assert.Throws<InvalidOrderTransitionException>(() => order.Accept(4));
        Assert.Throws<InvalidOrderTransitionException>(order.MarkUnknown);
        Assert.Throws<InvalidOrderTransitionException>(() => order.Reject("x"));
        Assert.Throws<InvalidOrderTransitionException>(order.MarkFundsBlocked);
        Assert.ThrowsAny<ArgumentException>(() => Place().Reject(" "));
        order.RequestCancel();
        Assert.Equal(OrderStatus.CancelRequested, order.Status);
    }

    [Fact]
    public void Restored_order_keeps_every_field()
    {
        var id = OrderId.New();

        var execution = new OrderExecution(4, 1000m, 3.9m, new EventPosition(1, 8));

        var order = Order.Restore(id, Client, "k", BuyLimit, 99m, OrderStatus.Rejected, 5, "busy", true, false, 2, 3, execution);

        Assert.Equal((id, Client, "k", BuyLimit, 99m), (order.Id, order.Client, order.ClientKey, order.Request, order.BlockedAmount));
        Assert.Same(execution, order.Execution);
        Assert.Throws<ArgumentNullException>(() =>
            Order.Restore(id, Client, "k", BuyLimit, 99m, OrderStatus.Rejected, 5, "busy", true, false, 2, 3, null!));
        Assert.Equal((OrderStatus.Rejected, (long?)5, "busy", true, false, 2, 3),
            (order.Status, order.ExchangeOrderId, order.RejectionReason, order.FundsBlocked, order.FundsReleased, order.SubmitAttempts, order.Version));
        Assert.Equal(("CDR", OrderSide.Buy, OrderType.Limit, (decimal?)250.40m, 10L),
            (order.Symbol, order.Side, order.Type, order.LimitPrice, order.Quantity));
        order.MarkSaved();
        Assert.Equal(4, order.Version);
    }

    [Fact]
    public void Each_illegal_move_names_its_action()
    {
        var accepted = Place();
        accepted.MarkFundsBlocked();
        accepted.Accept(1);

        string Action(Action move) => Assert.Throws<InvalidOrderTransitionException>(move).Message.Split(" cannot ")[1];

        Assert.Equal("block funds", Action(accepted.MarkFundsBlocked));
        Assert.Equal("submit", Action(accepted.BeginSubmission));
        Assert.Equal("mark unknown", Action(accepted.MarkUnknown));
        Assert.Equal("reject", Action(() => accepted.Reject("x")));
        Assert.Equal("release funds", Action(() => accepted.ReleaseEntry()));
        Assert.Equal("mark funds released", Action(accepted.MarkFundsReleased));
        Assert.Equal("cancel", Action(Place().RequestCancel));
    }

    [Fact]
    public void Submission_needs_blocked_funds_and_counts_attempts()
    {
        var order = Place();
        Assert.Throws<InvalidOrderTransitionException>(order.BeginSubmission);

        order.MarkFundsBlocked();
        order.BeginSubmission();
        order.MarkUnknown();
        order.BeginSubmission();

        Assert.Equal(2, order.SubmitAttempts);
    }
}
