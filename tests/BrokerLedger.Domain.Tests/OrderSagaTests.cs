using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public partial class OrderSagaTests
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private readonly ClientId _client = ClientId.New();
    private readonly InMemoryLedgerStore _ledger = new();
    private readonly InMemoryOrders _orders = new();
    private readonly ScriptedExchange _exchange = new();
    private readonly OrderSaga _saga;

    public OrderSagaTests()
    {
        _saga = new OrderSaga(_orders, _ledger, _exchange);
        _ledger.Ledger.Post(JournalEntries.Deposit(_client, Money.Pln(10_000m)));
    }

    private Order NewBuy(string key = "k-1") => Order.Place(OrderId.New(), _client, key,
        new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 250m, 10), Commission.Standard);

    /// <summary>Jak endpoint: zapis (albo istniejace zlecenie z tym kluczem), potem saga.</summary>
    private async Task<Order> PlaceAsync(Order order) => await _saga.ProgressAsync(await _orders.AddOrGetAsync(order));

    private decimal Available => _ledger.Ledger.Balance(AccountId.Available(_client, Pln));
    private decimal Blocked => _ledger.Ledger.Balance(AccountId.Blocked(_client, Pln));

    [Fact]
    public async Task Accepted_order_keeps_its_funds_blocked()
    {
        _exchange.Then(new ExchangeReply.Accepted(17));

        var order = await PlaceAsync(NewBuy());

        Assert.Equal((OrderStatus.Accepted, (long?)17), (order.Status, order.ExchangeOrderId));
        Assert.Equal((10_000m - 2509.75m, 2509.75m), (Available, Blocked));
        Assert.Equal([order.Id.ForExchange], _exchange.Placed);
        Assert.Equal(OrderStatus.Accepted, (await _orders.GetAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task Not_enough_money_never_reaches_the_exchange()
    {
        var order = await PlaceAsync(Order.Place(OrderId.New(), _client, "big",
            new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 2000m, 10), Commission.Standard));

        Assert.Equal((OrderStatus.Rejected, OrderSaga.InsufficientFunds, false), (order.Status, order.RejectionReason, order.FundsBlocked));
        Assert.Empty(_exchange.Placed);
        Assert.Equal(10_000m, Available);
        Assert.False(order.NeedsAttention);
    }

    [Fact]
    public async Task Refused_order_gets_its_money_back()
    {
        _exchange.Then(new ExchangeReply.Refused("unknown instrument"));

        var order = await PlaceAsync(NewBuy());

        Assert.Equal((OrderStatus.Rejected, "unknown instrument", true), (order.Status, order.RejectionReason, order.FundsReleased));
        Assert.Equal((10_000m, 0m), (Available, Blocked));
    }

    [Fact]
    public async Task Busy_exchange_on_the_first_try_is_a_rejection()
    {
        _exchange.Then(new ExchangeReply.Busy("queue full"));

        var order = await PlaceAsync(NewBuy());

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(0m, Blocked);
    }

    [Fact]
    public async Task No_answer_keeps_the_block_until_the_order_is_explained()
    {
        _exchange.Then(new ExchangeReply.NoAnswer("timeout")).Then(new ExchangeReply.Accepted(5));

        var unknown = await PlaceAsync(NewBuy());
        Assert.Equal(OrderStatus.Unknown, unknown.Status);
        Assert.Equal(2509.75m, Blocked);
        Assert.True(unknown.NeedsAttention);

        var explained = await _saga.ProgressAsync(unknown);

        Assert.Equal((OrderStatus.Accepted, (long?)5), (explained.Status, explained.ExchangeOrderId));
        Assert.Equal(2, _exchange.Placed.Count);
        Assert.All(_exchange.Placed, id => Assert.Equal(unknown.Id.ForExchange, id));
        Assert.Equal(2509.75m, Blocked);
    }

    [Fact]
    public async Task Busy_after_a_timeout_is_not_a_rejection()
    {
        // Pierwsza proba mogla jeszcze czekac w kolejce gieldy — zwolnienie srodkow byloby niebezpieczne.
        _exchange.Then(new ExchangeReply.NoAnswer("timeout")).Then(new ExchangeReply.Busy("queue full"));
        var order = await PlaceAsync(NewBuy());

        order = await _saga.ProgressAsync(order);

        Assert.Equal(OrderStatus.Unknown, order.Status);
        Assert.Equal(2509.75m, Blocked);
    }

    [Fact]
    public async Task Refusal_after_a_timeout_releases_the_money()
    {
        _exchange.Then(new ExchangeReply.NoAnswer("timeout")).Then(new ExchangeReply.Refused("bad request"));
        var order = await PlaceAsync(NewBuy());

        order = await _saga.ProgressAsync(order);

        Assert.Equal((OrderStatus.Rejected, true), (order.Status, order.FundsReleased));
        Assert.Equal(0m, Blocked);
    }

    [Fact]
    public async Task Rejection_for_lack_of_funds_is_stored_and_final()
    {
        var order = await PlaceAsync(Order.Place(OrderId.New(), _client, "big",
            new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 2000m, 10), Commission.Standard));

        var stored = (await _orders.GetAsync(order.Id))!;
        Assert.Equal(OrderStatus.Rejected, stored.Status);

        await _saga.ProgressAsync(stored);

        Assert.Empty(_exchange.Placed);
        Assert.Equal(10_000m, Available);
    }
}
