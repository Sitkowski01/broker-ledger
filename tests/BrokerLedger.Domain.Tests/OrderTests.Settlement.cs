using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public partial class OrderTests
{
    private const long ExchangeId = 40;

    private static Order Accepted(OrderRequest? request = null)
    {
        var order = Place(request);
        order.MarkFundsBlocked();
        order.Accept(ExchangeId, 1);
        return order;
    }

    /// <summary>Transakcja, w ktorej zlecenie jest aktywne (taker) po swojej stronie.</summary>
    private static ExchangeEvent.Trade Fill(Order order, long sequence, decimal price, long quantity) =>
        new("CDR", new EventPosition(1, sequence), 99, ExchangeId, order.Side, price, quantity);

    private static void Settle(Order order, ExchangeEvent.Trade trade)
    {
        order.TradeEntry(trade, Commission.Standard);
        order.ApplyTrade(trade, Commission.Standard);
    }

    [Fact]
    public void Partial_fills_add_up_and_the_last_one_fills_the_order()
    {
        var order = Accepted();

        Settle(order, Fill(order, 3, 250m, 4));
        Assert.Equal((OrderStatus.Accepted, 6L), (order.Status, order.RemainingQuantity));
        Settle(order, Fill(order, 7, 250.40m, 6));

        // 1000 + 1502,40 = 2502,40; prowizja raz od calosci: 9,76 (a nie 3 + 5,86 od kazdej czesci osobno).
        Assert.Equal(new OrderExecution(10, 2502.40m, 9.76m, new EventPosition(1, 7)), order.Execution);
        Assert.Equal((OrderStatus.Filled, true, false), (order.Status, order.AwaitsRelease, order.IsOpenOnExchange));
        Assert.Equal(2513.77m - 2502.40m - 9.76m, order.RemainingBlocked);
        Assert.Equal(JournalEntries.Release(Client, Asset.Cash(Currency.Pln), 1.61m).Postings, order.ReleaseEntry()!.Postings);
        Assert.True(order.NeedsAttention);
    }

    [Fact]
    public void Each_fill_pays_only_the_commission_it_adds()
    {
        var order = Accepted();

        var first = order.TradeEntry(Fill(order, 1, 250m, 1), Commission.Standard);
        order.ApplyTrade(Fill(order, 1, 250m, 1), Commission.Standard);
        var second = order.TradeEntry(Fill(order, 2, 250m, 1), Commission.Standard);

        Assert.Equal(JournalEntries.Purchase(Client, "CDR", 1, 250m, 3m).Postings, first.Postings);
        // Po 500 zl prowizja to nadal minimum 3 zl — juz pobrane, wiec druga transakcja nie placi nic.
        Assert.Equal(JournalEntries.Purchase(Client, "CDR", 1, 250m, 0m).Postings, second.Postings);
    }

    [Fact]
    public void Sale_settles_shares_and_has_nothing_left_to_release()
    {
        var order = Accepted(new OrderRequest("CDR", OrderSide.Sell, OrderType.Market, null, 5));
        var trade = new ExchangeEvent.Trade("CDR", new EventPosition(1, 2), ExchangeId, 99, OrderSide.Buy, 100m, 5);

        Assert.Equal(JournalEntries.Sale(Client, "CDR", 5, 500m, 3m).Postings, order.TradeEntry(trade, Commission.Standard).Postings);
        order.ApplyTrade(trade, Commission.Standard);

        Assert.Equal((OrderStatus.Filled, 0m), (order.Status, order.RemainingBlocked));
        Assert.Null(order.ReleaseEntry());
        order.MarkFundsReleased();
        Assert.False(order.NeedsAttention);
    }

    [Fact]
    public void Cancellation_returns_what_was_not_used()
    {
        var order = Accepted();
        Settle(order, Fill(order, 1, 250m, 4));
        order.RequestCancel();

        order.ApplyCancellation(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, 2), ExchangeId, 6, "REQUESTED"));

        Assert.Equal((OrderStatus.Cancelled, new EventPosition(1, 2), 4L), (order.Status, order.Execution.LastEvent, order.Execution.Quantity));
        Assert.Equal(2513.77m - 1000m - 3.90m, order.RemainingBlocked);
        Assert.True(order.AwaitsRelease);
        order.MarkFundsReleased();
        Assert.Equal((false, false), (order.AwaitsRelease, order.NeedsAttention));
    }

    [Fact]
    public void Remembers_the_last_settled_event()
    {
        var order = Accepted();
        Settle(order, Fill(order, 5, 250m, 1));

        Assert.True(order.Execution.Includes(new EventPosition(1, 5)));
        Assert.True(order.Execution.Includes(new EventPosition(1, 4)));
        Assert.False(order.Execution.Includes(new EventPosition(1, 6)));
        Assert.False(OrderExecution.None.Includes(new EventPosition(1, 1)));
        Assert.Throws<ArgumentNullException>(() => OrderExecution.None.Includes(null!));
        Assert.Throws<InvalidOrderTransitionException>(() => order.ApplyTrade(Fill(order, 5, 250m, 1), Commission.Standard));
        Assert.Equal($"order:{order.Id}:trade:1:5", order.TradeKey(new EventPosition(1, 5)));
        Assert.Throws<ArgumentNullException>(() => order.TradeKey(null!));
    }

    [Fact]
    public void Refuses_events_that_do_not_fit_the_order()
    {
        var order = Accepted();

        string Problem(Action settle) => Assert.Throws<ExchangeEventMismatchException>(settle).Message.Split(": ", 2)[1];

        Assert.Equal("the order is on the Buy side", Problem(() =>
            order.TradeEntry(new ExchangeEvent.Trade("CDR", new EventPosition(1, 1), 99, ExchangeId, OrderSide.Sell, 1m, 1), Commission.Standard)));
        Assert.Equal("traded 11, but only 10 was open", Problem(() => order.ApplyTrade(Fill(order, 1, 1m, 11), Commission.Standard)));
        Assert.Equal("bought at 250.41, above the limit 250.40", Problem(() => order.ApplyTrade(Fill(order, 1, 250.41m, 1), Commission.Standard)));
        var sell = Accepted(new OrderRequest("CDR", OrderSide.Sell, OrderType.Limit, 100m, 5));
        Assert.Equal("sold at 99.99, below the limit 100", Problem(() => sell.ApplyTrade(Fill(sell, 1, 99.99m, 1), Commission.Standard)));
        sell.ApplyTrade(Fill(sell, 1, 100m, 1), Commission.Standard);
        Assert.Equal(1L, sell.Execution.Quantity);
        Assert.Equal("it is about PKN orders 99, 41", Problem(() =>
            order.ApplyTrade(new ExchangeEvent.Trade("PKN", new EventPosition(1, 1), 99, 41, OrderSide.Buy, 1m, 1), Commission.Standard)));
        Assert.Equal("it is about CDR orders 41", Problem(() =>
            order.ApplyCancellation(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, 1), 41, 10, "REQUESTED"))));
        Assert.Equal("exchange cancelled 9, but 10 was open", Problem(() =>
            order.ApplyCancellation(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, 1), ExchangeId, 9, "REQUESTED"))));
        Assert.Equal("the order was accepted in exchange run 1", Problem(() => order.ApplyTrade(
            new ExchangeEvent.Trade("CDR", new EventPosition(2, 1), 99, ExchangeId, OrderSide.Buy, 1m, 1), Commission.Standard)));
        Assert.Equal((OrderStatus.Accepted, OrderExecution.None), (order.Status, order.Execution));
        // Kupno dokladnie po limicie i calosc naraz jest w porzadku.
        Settle(order, Fill(order, 1, 250.40m, 10));
        Assert.Equal(OrderStatus.Filled, order.Status);
    }

    [Fact]
    public void Exchange_restart_closes_the_order_and_frees_what_is_left()
    {
        var order = Accepted();
        Settle(order, Fill(order, 1, 250m, 4));

        Assert.Throws<InvalidOrderTransitionException>(() => order.CloseAfterExchangeRestart(1));
        order.CloseAfterExchangeRestart(2);

        Assert.Equal((OrderStatus.Cancelled, true, 6L), (order.Status, order.AwaitsRelease, order.RemainingQuantity));
        Assert.Equal("close after an exchange restart", Assert.Throws<InvalidOrderTransitionException>(
            () => order.CloseAfterExchangeRestart(3)).Message.Split(" cannot ")[1]);
        // Bez przebiegu (zlecenie sprzed kolumny) nie wiadomo, czy gielda je jeszcze zna.
        var legacy = Order.Restore(OrderId.New(), Client, "k", BuyLimit, 99m, OrderStatus.Accepted, 5, null, null, true, false, 1, 0,
            OrderExecution.None);
        Assert.Throws<InvalidOrderTransitionException>(() => legacy.CloseAfterExchangeRestart(2));
    }

    [Fact]
    public void Only_orders_on_the_exchange_can_be_settled()
    {
        var pending = Place();
        var trade = Fill(pending, 1, 1m, 1);

        Assert.Equal("settle a trade", Assert.Throws<InvalidOrderTransitionException>(
            () => pending.TradeEntry(trade, Commission.Standard)).Message.Split(" cannot ")[1]);
        Assert.Equal("settle a cancellation", Assert.Throws<InvalidOrderTransitionException>(
            () => pending.ApplyCancellation(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, 1), 1, 1, "X"))).Message.Split(" cannot ")[1]);
        Assert.Throws<ArgumentNullException>(() => Accepted().ApplyTrade(null!, Commission.Standard));
        Assert.Throws<ArgumentNullException>(() => Accepted().ApplyTrade(trade, null!));
        Assert.Throws<ArgumentNullException>(() => Accepted().ApplyCancellation(null!));
        Assert.Equal((false, true), (pending.IsOpenOnExchange, Accepted().IsOpenOnExchange));
    }

    [Fact]
    public void Order_is_in_flight_from_the_first_attempt_until_the_exchange_answers()
    {
        var order = Place();
        order.MarkFundsBlocked();
        Assert.False(order.IsInFlight);

        order.BeginSubmission();
        Assert.True(order.IsInFlight);
        order.MarkUnknown();
        Assert.True(order.IsInFlight);
        order.Accept(ExchangeId, 1);
        Assert.False(order.IsInFlight);
    }
}
