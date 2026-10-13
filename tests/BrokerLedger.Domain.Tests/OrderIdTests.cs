using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class OrderIdTests
{
    [Fact]
    public void New_id_carries_the_moment_it_was_made()
    {
        var before = DateTimeOffset.UtcNow.AddMilliseconds(-1);
        var id = OrderId.New();
        var after = DateTimeOffset.UtcNow.AddMilliseconds(1);

        Assert.InRange(id.CreatedAt!.Value, before, after);
    }

    [Fact]
    public void Time_is_read_from_the_first_48_bits_of_a_version_7_uuid()
    {
        // 0x019a0b5c7e40 ms od 1970 = 2025-10-22 10:00:02.368 UTC, potem wersja 7 i losowe bity.
        var id = new OrderId(Guid.Parse("019a0b5c-7e40-7abc-8def-0123456789ab"));

        Assert.Equal(new DateTimeOffset(2025, 10, 22, 10, 0, 2, 368, TimeSpan.Zero), id.CreatedAt);
    }

    [Fact]
    public void Older_ids_without_a_timestamp_have_no_creation_time()
    {
        Assert.Null(new OrderId(Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301")).CreatedAt);
    }
}

public class LedgerKeyTests
{
    [Fact]
    public void Every_ledger_key_of_an_order_points_back_to_it()
    {
        var order = Order.Place(OrderId.New(), ClientId.New(), "k", new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 10m, 1), Commission.Standard);

        Assert.All(
            [order.BlockKey, order.ReleaseKey, order.TradeKey(new EventPosition(1, 7))],
            key => Assert.Equal(order.Id, Order.OfLedgerKey(key)));
    }

    [Theory]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301:dep-1")]
    [InlineData("order:not-a-guid:block")]
    [InlineData("order:00000000-0000-0000-0000-000000000000:block")]
    [InlineData("order:3f2504e0-4f89-41d3-9a0c-0305e82c3301")]
    [InlineData("deposit:3f2504e0-4f89-41d3-9a0c-0305e82c3301:x")]
    public void Deposits_and_odd_keys_have_no_order(string key)
    {
        Assert.Null(Order.OfLedgerKey(key));
        Assert.Throws<ArgumentNullException>(() => Order.OfLedgerKey(null!));
    }
}

public class QuoteTests
{
    [Theory]
    [InlineData(101, 100, 1)]
    [InlineData(99.99, 100, -1)]
    [InlineData(100, 100, 0)]
    public void Direction_compares_with_the_previous_trade(decimal price, decimal previous, int direction) =>
        Assert.Equal(direction, new Quote("CDR", price, previous, DateTimeOffset.UnixEpoch).Direction);

    [Fact]
    public void First_trade_has_no_direction() =>
        Assert.Equal(0, new Quote("CDR", 100m, null, DateTimeOffset.UnixEpoch).Direction);
}
