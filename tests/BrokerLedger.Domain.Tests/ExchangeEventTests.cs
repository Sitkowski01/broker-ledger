using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class ExchangeEventTests
{
    private static readonly EventPosition At = new(1, 1);

    private static ExchangeEvent.Trade Trade(long maker = 1, long taker = 2, decimal price = 10m, long quantity = 5) =>
        new("CDR", At, maker, taker, OrderSide.Buy, price, quantity);

    [Fact]
    public void Positions_are_ordered_by_run_then_sequence()
    {
        Assert.True(new EventPosition(1, 5).IsAtOrAfter(new EventPosition(1, 5)));
        Assert.True(new EventPosition(1, 6).IsAtOrAfter(new EventPosition(1, 5)));
        Assert.False(new EventPosition(1, 4).IsAtOrAfter(new EventPosition(1, 5)));
        // Nowy przebieg gieldy liczy od 1, ale jest pozniej niz wszystko z poprzedniego.
        Assert.True(new EventPosition(2, 1).IsAtOrAfter(new EventPosition(1, 900)));
        Assert.False(new EventPosition(1, 900).IsAtOrAfter(new EventPosition(2, 1)));
        Assert.Throws<ArgumentNullException>(() => At.IsAtOrAfter(null!));
        Assert.Equal("3:14", new EventPosition(3, 14).ToString());
    }

    [Fact]
    public void Position_needs_positive_numbers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventPosition(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventPosition(1, 0));
        Assert.Equal((1L, 1L), (new EventPosition(1, 1).RunId, new EventPosition(1, 1).Sequence));
    }

    [Fact]
    public void Maker_is_on_the_other_side_than_the_taker()
    {
        var trade = new ExchangeEvent.Trade("CDR", At, 7, 9, OrderSide.Sell, 250.40m, 3);

        Assert.Equal((OrderSide.Sell, OrderSide.Buy), (trade.SideOf(9), trade.SideOf(7)));
        Assert.Equal(OrderSide.Sell, Trade().SideOf(1));
        Assert.Equal([7L, 9L], trade.ExchangeOrderIds);
        Assert.Equal(751.20m, trade.Value);
        Assert.Contains("order 8 is not part", Assert.Throws<ArgumentException>(() => trade.SideOf(8)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 2, "10", 1)]
    [InlineData(1, 0, "10", 1)]
    [InlineData(1, 1, "10", 1)]
    [InlineData(1, 2, "0", 1)]
    [InlineData(1, 2, "10.001", 1)]
    [InlineData(1, 2, "10000000.01", 1)]
    [InlineData(1, 2, "10", 0)]
    public void Refuses_impossible_trades(long maker, long taker, string price, long quantity) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            Trade(maker, taker, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), quantity));

    [Fact]
    public void Impossible_trades_say_why()
    {
        Assert.StartsWith("an order cannot trade with itself", Assert.Throws<ArgumentException>(() => Trade(3, 3)).Message, StringComparison.Ordinal);
        Assert.StartsWith("price must be in whole grosze, at most 10000000",
            Assert.Throws<ArgumentOutOfRangeException>(() => Trade(price: 0.001m)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Trade_keeps_its_fields_and_normal_limits()
    {
        var trade = Trade(price: Order.MaxPrice, quantity: 1);

        Assert.Equal(("CDR", At, 1L, 2L, OrderSide.Buy, Order.MaxPrice, 1L),
            (trade.Symbol, trade.Position, trade.MakerOrderId, trade.TakerOrderId, trade.TakerSide, trade.Price, trade.Quantity));
        Assert.Equal(0.01m, Trade(price: 0.01m).Price);
        Assert.Throws<ArgumentException>(() => new ExchangeEvent.Trade("cdr", At, 1, 2, OrderSide.Buy, 1m, 1));
        Assert.Throws<ArgumentNullException>(() => new ExchangeEvent.Trade("CDR", null!, 1, 2, OrderSide.Buy, 1m, 1));
    }

    [Fact]
    public void Cancellation_names_the_order_and_what_was_left()
    {
        var cancelled = new ExchangeEvent.Cancelled("CDR", At, 4, 6, "NO_LIQUIDITY");

        Assert.Equal((4L, 6L, "NO_LIQUIDITY"), (cancelled.OrderId, cancelled.Quantity, cancelled.Reason));
        Assert.Equal([4L], cancelled.ExchangeOrderIds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExchangeEvent.Cancelled("CDR", At, 0, 6, "X"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExchangeEvent.Cancelled("CDR", At, 4, 0, "X"));
        Assert.ThrowsAny<ArgumentException>(() => new ExchangeEvent.Cancelled("CDR", At, 4, 6, " "));
        Assert.Equal(32, new ExchangeEvent.Cancelled("CDR", At, 4, 6, new string('R', 32)).Reason.Length);
        Assert.Contains("at most 32", Assert.Throws<ArgumentException>(
            () => new ExchangeEvent.Cancelled("CDR", At, 4, 6, new string('R', 33))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mismatch_names_the_order_and_the_event()
    {
        var order = OrderId.New();

        var ex = new ExchangeEventMismatchException(order, new EventPosition(2, 5), "wrong side");

        Assert.Equal($"event 2:5 does not fit order {order}: wrong side", ex.Message);
        Assert.Equal(order, ex.Order);
    }

}
