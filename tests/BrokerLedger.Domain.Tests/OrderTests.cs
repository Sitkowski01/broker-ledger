using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public partial class OrderTests
{
    private static readonly ClientId Client = ClientId.New();
    private static readonly OrderRequest BuyLimit = new("CDR", OrderSide.Buy, OrderType.Limit, 250.40m, 10);

    private static Order Place(OrderRequest? request = null) =>
        Order.Place(OrderId.New(), Client, "key-1", request ?? BuyLimit, Commission.Standard);

    [Fact]
    public void Commission_is_a_percentage_with_a_minimum_rounded_up_to_a_grosz()
    {
        Assert.Equal(Money.Pln(9.77m), Commission.Standard.For(Money.Pln(2504m)));   // 9,7656 -> 9,77
        Assert.Equal(Money.Pln(3m), Commission.Standard.For(Money.Pln(100m)));       // 0,39 < 3
        Assert.Equal(Money.Pln(3.02m), Commission.Standard.For(Money.Pln(771.80m))); // 3,01002 -> w gore 3,02
        Assert.Equal(Money.Pln(0m), new Commission(0m, Money.Pln(0m)).For(Money.Pln(5m)));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1")]
    public void Commission_rate_must_be_a_fraction(string rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Commission(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), Money.Pln(1m)));

    [Fact]
    public void Commission_minimum_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Commission(0.01m, Money.Pln(-1m)));
        Assert.Throws<ArgumentNullException>(() => new Commission(0.01m, null!));
        Assert.Throws<ArgumentNullException>(() => Commission.Standard.For(null!));
    }

    [Fact]
    public void Buy_blocks_value_plus_commission_in_cash()
    {
        var order = Place();

        Assert.Equal(2504m + 9.77m, order.BlockedAmount);
        Assert.Equal(Asset.Cash(Currency.Pln), order.BlockedAsset);
        Assert.Equal(JournalEntries.Block(Client, Asset.Cash(Currency.Pln), 2513.77m).Postings, order.BlockEntry().Postings);
        Assert.Equal((OrderStatus.Pending, false, false, 0), (order.Status, order.FundsBlocked, order.FundsReleased, order.Version));
    }

    [Fact]
    public void Sell_blocks_the_shares()
    {
        var order = Place(new OrderRequest("CDR", OrderSide.Sell, OrderType.Market, null, 7));

        Assert.Equal((7m, Asset.Shares("CDR")), (order.BlockedAmount, order.BlockedAsset));
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderType.Market, null, 1, "market buy is not supported")]
    [InlineData(OrderSide.Sell, OrderType.Market, "10", 1, "market order must not have a price")]
    [InlineData(OrderSide.Buy, OrderType.Limit, null, 1, "limit order needs a price")]
    [InlineData(OrderSide.Buy, OrderType.Limit, "0", 1, "limit order needs a price")]
    [InlineData(OrderSide.Buy, OrderType.Limit, "10.001", 1, "limit order needs a price")]
    [InlineData(OrderSide.Buy, OrderType.Limit, "10000000.01", 1, "limit order needs a price")]
    [InlineData(OrderSide.Buy, OrderType.Limit, "10000000", 100_000, "order value exceeds")]
    [InlineData(OrderSide.Sell, OrderType.Limit, "10", 0, "quantity must be 1-1000000000")]
    [InlineData(OrderSide.Sell, OrderType.Limit, "10", 1_000_000_001, "quantity must be 1-1000000000")]
    public void Refuses_orders_the_broker_cannot_block(OrderSide side, OrderType type, string? price, long quantity, string message)
    {
        var limit = price is null ? (decimal?)null : decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        var ex = Assert.ThrowsAny<ArgumentException>(() => Place(new OrderRequest("CDR", side, type, limit, quantity)));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Limits_are_inclusive()
    {
        Assert.Equal(Order.MaxQuantity, Place(new OrderRequest("CDR", OrderSide.Sell, OrderType.Limit, Order.MaxPrice, Order.MaxQuantity)).Quantity);
        Assert.Equal(0.01m, Place(BuyLimit with { LimitPrice = 0.01m, Quantity = 1 }).LimitPrice);
    }

    [Fact]
    public void Validates_client_key_and_symbol()
    {
        Assert.ThrowsAny<ArgumentException>(() => Order.Place(OrderId.New(), Client, " ", BuyLimit, Commission.Standard));
        Assert.Contains("at most 100", Assert.Throws<ArgumentException>(() =>
            Order.Place(OrderId.New(), Client, new string('k', 101), BuyLimit, Commission.Standard)).Message, StringComparison.Ordinal);
        Assert.Equal(100, Order.Place(OrderId.New(), Client, new string('k', 100), BuyLimit, Commission.Standard).ClientKey.Length);
        Assert.Throws<ArgumentException>(() => Place(BuyLimit with { Symbol = "cdr" }));
        Assert.Throws<ArgumentNullException>(() => Order.Place(null!, Client, "k", BuyLimit, Commission.Standard));
        Assert.Throws<ArgumentNullException>(() => Order.Place(OrderId.New(), null!, "k", BuyLimit, Commission.Standard));
        Assert.Throws<ArgumentNullException>(() => Order.Place(OrderId.New(), Client, "k", null!, Commission.Standard));
        Assert.Throws<ArgumentNullException>(() => Order.Place(OrderId.New(), Client, "k", BuyLimit, null!));
    }

    [Fact]
    public void Ids_and_keys_are_stable()
    {
        var id = new OrderId(Guid.Parse("0199d6a1-0000-7000-8000-000000000001"));
        var order = Order.Place(id, Client, "k", BuyLimit, Commission.Standard);

        Assert.Equal("0199d6a100007000800000000000000" + "1", id.ForExchange);
        Assert.Equal($"order:{id}:block", order.BlockKey);
        Assert.Equal($"order:{id}:release", order.ReleaseKey);
        Assert.Equal("0199d6a1-0000-7000-8000-000000000001", id.ToString());
        Assert.Throws<ArgumentException>(() => new OrderId(Guid.Empty));
        Assert.NotEqual(OrderId.New(), OrderId.New());
    }

    [Fact]
    public void Errors_say_what_is_wrong()
    {
        Assert.Contains("rate must be in [0, 1)", Assert.Throws<ArgumentOutOfRangeException>(() => new Commission(1m, Money.Pln(1m))).Message, StringComparison.Ordinal);
        Assert.Contains("minimum must not be negative", Assert.Throws<ArgumentOutOfRangeException>(() => new Commission(0m, Money.Pln(-1m))).Message, StringComparison.Ordinal);
        Assert.Contains("order id must not be empty", Assert.Throws<ArgumentException>(() => new OrderId(Guid.Empty)).Message, StringComparison.Ordinal);
        var id = OrderId.New();
        Assert.Equal($"order {id} was changed by someone else", new OrderConcurrencyException(id).Message);
    }
}
