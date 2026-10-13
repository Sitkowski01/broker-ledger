using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class TradeSettlementTests
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private static readonly Asset Cdr = Asset.Shares("CDR");
    private readonly ClientId _client = ClientId.New();
    private readonly InMemoryLedgerStore _ledger = new();
    private readonly InMemoryOrders _orders = new();
    private readonly InMemoryInbox _inbox = new();
    private readonly TradeSettlement _settlement;

    public TradeSettlementTests()
    {
        _settlement = new TradeSettlement(_orders, _ledger, _inbox, Commission.Standard);
        _ledger.Ledger.Post(JournalEntries.Deposit(_client, Money.Pln(10_000m)));
        _ledger.Ledger.Post(JournalEntries.SharesTransferIn(_client, "CDR", 50));
    }

    private decimal Balance(AccountId account) => _ledger.Ledger.Balance(account);

    /// <summary>Zlecenie przyjete przez gielde z numerem <paramref name="exchangeId"/>, srodki zablokowane.</summary>
    private async Task<Order> AcceptedAsync(long exchangeId, OrderSide side = OrderSide.Buy, long quantity = 10, string key = "k")
    {
        var request = side == OrderSide.Buy
            ? new OrderRequest("CDR", side, OrderType.Limit, 250m, quantity)
            : new OrderRequest("CDR", side, OrderType.Limit, 240m, quantity);
        var order = Order.Place(OrderId.New(), _client, key, request, Commission.Standard);
        await _ledger.PostAsync(order.BlockKey, order.BlockEntry());
        order.MarkFundsBlocked();
        order.Accept(exchangeId);
        _orders.Put(await _orders.AddOrGetAsync(order));
        _orders.Put(order);
        return order;
    }

    private static ExchangeEvent.Trade Trade(long sequence, long maker, long taker, long quantity, decimal price = 250m,
        OrderSide takerSide = OrderSide.Buy, string symbol = "CDR") =>
        new(symbol, new EventPosition(1, sequence), maker, taker, takerSide, price, quantity);

    private async Task<Order> ReadAsync(Order order) => (await _orders.GetAsync(order.Id))!;

    [Fact]
    public async Task Full_fill_moves_cash_and_shares_and_returns_the_unused_block()
    {
        var order = await AcceptedAsync(7);

        // Kupno po 240 zl przy limicie 250: blokada liczona od 250, wiec reszta wraca klientowi.
        var outcome = await _settlement.SettleAsync(Trade(1, maker: 99, taker: 7, quantity: 10, price: 240m));

        Assert.Equal(SettleOutcome.Settled, outcome);
        var settled = await ReadAsync(order);
        Assert.Equal((OrderStatus.Filled, true, 10L), (settled.Status, settled.FundsReleased, settled.Execution.Quantity));
        Assert.Equal(10_000m - 2400m - 9.36m, Balance(AccountId.Available(_client, Pln)));
        Assert.Equal(0m, Balance(AccountId.Blocked(_client, Pln)));
        Assert.Equal(60m, Balance(AccountId.Available(_client, Cdr)));
        Assert.Equal(9.36m, Balance(AccountId.FeeIncome(Currency.Pln)));
    }

    [Fact]
    public async Task Partial_fill_keeps_the_rest_blocked()
    {
        var order = await AcceptedAsync(7);

        await _settlement.SettleAsync(Trade(1, 99, 7, 4));

        var partial = await ReadAsync(order);
        Assert.Equal((OrderStatus.Accepted, false, 6L), (partial.Status, partial.FundsReleased, partial.RemainingQuantity));
        Assert.Equal(order.BlockedAmount - 1000m - 3.90m, Balance(AccountId.Blocked(_client, Pln)));
    }

    [Fact]
    public async Task Cancellation_releases_what_is_left()
    {
        var order = await AcceptedAsync(7, OrderSide.Sell);
        await _settlement.SettleAsync(Trade(1, maker: 7, taker: 99, quantity: 3));

        var outcome = await _settlement.SettleAsync(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, 2), 7, 7, "REQUESTED"));

        Assert.Equal(SettleOutcome.Settled, outcome);
        Assert.Equal((OrderStatus.Cancelled, true), ((await ReadAsync(order)).Status, (await ReadAsync(order)).FundsReleased));
        Assert.Equal((47m, 0m), (Balance(AccountId.Available(_client, Cdr)), Balance(AccountId.Blocked(_client, Cdr))));
        Assert.Equal(10_000m + 750m - 3m, Balance(AccountId.Available(_client, Pln)));
    }

    [Fact]
    public async Task Trade_between_two_clients_of_the_broker_settles_both_sides()
    {
        var buy = await AcceptedAsync(7, OrderSide.Buy, 5, "buy");
        var sell = await AcceptedAsync(8, OrderSide.Sell, 5, "sell");

        await _settlement.SettleAsync(Trade(1, maker: 8, taker: 7, quantity: 5));

        Assert.Equal((OrderStatus.Filled, OrderStatus.Filled), ((await ReadAsync(buy)).Status, (await ReadAsync(sell)).Status));
        // Akcje przeszly od klienta do klienta, depozyt i bank wrocily do stanu sprzed transakcji.
        Assert.Equal(50m, Balance(AccountId.Available(_client, Cdr)));
        Assert.Equal(50m, Balance(AccountId.Depository("CDR")));
        Assert.Equal(10_000m - 2 * 4.88m, Balance(AccountId.Available(_client, Pln)));
    }

    [Fact]
    public async Task Events_of_other_participants_are_not_ours()
    {
        await AcceptedAsync(7);

        Assert.Equal(SettleOutcome.NotOurs, await _settlement.SettleAsync(Trade(1, 98, 99, 5)));
        Assert.Equal(SettleOutcome.NotOurs, await _settlement.SettleAsync(Trade(2, 98, 7, 5, symbol: "PKN")));
        Assert.Equal(10_000m, Balance(AccountId.Available(_client, Pln)) + Balance(AccountId.Blocked(_client, Pln)));
    }

    [Fact]
    public async Task Waits_while_an_order_on_the_symbol_has_no_exchange_number_yet()
    {
        var known = await AcceptedAsync(7);
        var inFlight = Order.Place(OrderId.New(), _client, "in-flight",
            new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 10m, 1), Commission.Standard);
        inFlight.MarkFundsBlocked();
        inFlight.BeginSubmission();
        inFlight.MarkUnknown();
        _orders.Put(inFlight);

        // Maker 99 moze byc zleceniem "w drodze" — nie wiadomo, wiec czekamy. Nasza strona (taker 7) juz rozliczona.
        Assert.Equal(SettleOutcome.Waiting, await _settlement.SettleAsync(Trade(1, 99, 7, 4)));
        Assert.Equal(4L, (await ReadAsync(known)).Execution.Quantity);
        Assert.Equal(SettleOutcome.NotOurs, await _settlement.SettleAsync(Trade(2, 98, 99, 1, symbol: "PKN")));

        inFlight.Accept(55);
        _orders.Put(inFlight);
        Assert.Equal(SettleOutcome.Settled, await _settlement.SettleAsync(Trade(1, 99, 7, 4)));
        Assert.Equal(4L, (await ReadAsync(known)).Execution.Quantity);
    }

    [Fact]
    public async Task Event_with_both_sides_found_does_not_wait_for_an_order_in_flight()
    {
        await AcceptedAsync(7, OrderSide.Buy, 5, "buy");
        await AcceptedAsync(8, OrderSide.Sell, 5, "sell");
        var inFlight = Order.Place(OrderId.New(), _client, "in-flight",
            new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 10m, 1), Commission.Standard);
        inFlight.MarkFundsBlocked();
        inFlight.BeginSubmission();
        _orders.Put(inFlight);

        // Obie strony to znane zlecenia, wiec zlecenie "w drodze" nie moze byc zadna z nich.
        Assert.Equal(SettleOutcome.Settled, await _settlement.SettleAsync(Trade(1, maker: 8, taker: 7, quantity: 5)));
    }

    [Fact]
    public async Task Repeat_goes_to_the_order_that_settled_it_even_if_a_newer_one_has_the_number()
    {
        var settled = await AcceptedAsync(7, quantity: 5, key: "settled");
        await _settlement.SettleAsync(Trade(1, 99, 7, 2));
        // Gielda ruszyla bez wznowienia i dala ten sam numer nowemu zleceniu; potem wraca powtorka starego zdarzenia.
        var newer = await AcceptedAsync(7, quantity: 5, key: "newer");

        await _settlement.SettleAsync(Trade(1, 99, 7, 2));

        Assert.Equal((2L, 0L), ((await ReadAsync(settled)).Execution.Quantity, (await ReadAsync(newer)).Execution.Quantity));
    }

    [Fact]
    public async Task Repeated_event_changes_nothing()
    {
        var order = await AcceptedAsync(7);
        await _settlement.SettleAsync(Trade(1, 99, 7, 4));
        var blocked = Balance(AccountId.Blocked(_client, Pln));

        Assert.Equal(SettleOutcome.Settled, await _settlement.SettleAsync(Trade(1, 99, 7, 4)));

        Assert.Equal((4L, blocked), ((await ReadAsync(order)).Execution.Quantity, Balance(AccountId.Blocked(_client, Pln))));
    }

    [Fact]
    public async Task Crash_after_posting_is_finished_by_the_repeat()
    {
        var order = await AcceptedAsync(7);
        _orders.FailOnSave = _orders.Saves + 1;

        await Assert.ThrowsAsync<TimeoutException>(() => _settlement.SettleAsync(Trade(1, 99, 7, 10)));
        Assert.Equal(OrderStatus.Accepted, (await ReadAsync(order)).Status);

        // Dokument jest juz w ksiedze; powtorka liczy go z tego samego stanu, wiec ksiega odpowiada "juz bylo".
        Assert.Equal(SettleOutcome.Settled, await _settlement.SettleAsync(Trade(1, 99, 7, 10)));
        Assert.Equal((OrderStatus.Filled, true), ((await ReadAsync(order)).Status, (await ReadAsync(order)).FundsReleased));
        Assert.Equal(10_000m - 2500m - 9.75m, Balance(AccountId.Available(_client, Pln)));
    }

    [Fact]
    public async Task Crash_before_the_release_is_finished_by_the_repeat()
    {
        var order = await AcceptedAsync(7);
        _ledger.FailOnce = order.ReleaseKey;

        await Assert.ThrowsAsync<TimeoutException>(() => _settlement.SettleAsync(Trade(1, 99, 7, 10, price: 200m)));
        Assert.Equal((OrderStatus.Filled, false), ((await ReadAsync(order)).Status, (await ReadAsync(order)).FundsReleased));

        Assert.Equal(SettleOutcome.Settled, await _settlement.SettleAsync(Trade(1, 99, 7, 10, price: 200m)));
        Assert.True((await ReadAsync(order)).FundsReleased);
        Assert.Equal(0m, Balance(AccountId.Blocked(_client, Pln)));
    }

    [Fact]
    public async Task Finished_order_with_a_recycled_number_is_not_matched_again()
    {
        var old = await AcceptedAsync(7, quantity: 1, key: "old");
        await _settlement.SettleAsync(Trade(1, 99, 7, 1));
        var current = await AcceptedAsync(7, quantity: 2, key: "new");

        await _settlement.SettleAsync(new ExchangeEvent.Trade("CDR", new EventPosition(2, 1), 99, 7, OrderSide.Buy, 250m, 2));

        Assert.Equal((1L, 2L), ((await ReadAsync(old)).Execution.Quantity, (await ReadAsync(current)).Execution.Quantity));
    }

    [Fact]
    public async Task Of_two_open_orders_with_one_number_the_newer_one_is_settled()
    {
        // Zlecenie z poprzedniego przebiegu gieldy zostalo otwarte u brokera, choc gieldy juz go nie zna.
        var stale = await AcceptedAsync(7, quantity: 5, key: "stale");
        var current = await AcceptedAsync(7, quantity: 5, key: "current");

        await _settlement.SettleAsync(new ExchangeEvent.Trade("CDR", new EventPosition(2, 1), 99, 7, OrderSide.Buy, 250m, 2));

        Assert.Equal((0L, 2L), ((await ReadAsync(stale)).Execution.Quantity, (await ReadAsync(current)).Execution.Quantity));
    }

    [Fact]
    public async Task Later_event_of_another_order_does_not_count_as_this_one_settled()
    {
        // Zdarzenie (1, 900) czeka w skrzynce, a nowsze zlecenie z tym samym numerem rozliczylo juz (2, 3).
        var older = await AcceptedAsync(5, quantity: 3, key: "older");
        var newer = await AcceptedAsync(5, quantity: 3, key: "newer");
        await _settlement.SettleAsync(new ExchangeEvent.Trade("CDR", new EventPosition(2, 3), 99, 5, OrderSide.Buy, 250m, 1));

        await _settlement.SettleAsync(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, 900), 5, 3, "REQUESTED"));

        Assert.Equal((OrderStatus.Cancelled, 1L), ((await ReadAsync(older)).Status, (await ReadAsync(newer)).Execution.Quantity));
    }

    [Fact]
    public async Task Order_accepted_while_its_trade_is_being_matched_is_not_taken_for_a_stranger()
    {
        var sending = Order.Place(OrderId.New(), _client, "sending",
            new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 250m, 2), Commission.Standard);
        await _ledger.PostAsync(sending.BlockKey, sending.BlockEntry());
        sending.MarkFundsBlocked();
        sending.BeginSubmission();
        _orders.Put(sending);
        // Odpowiedz REST przychodzi dokladnie miedzy wyszukaniem po numerze a dalszymi krokami rozliczenia.
        _orders.AfterLookup = () =>
        {
            sending.Accept(55);
            _orders.Put(sending);
            _orders.AfterLookup = null;
        };

        Assert.Equal(SettleOutcome.Waiting, await _settlement.SettleAsync(Trade(1, 99, 55, 2)));
        Assert.Equal(SettleOutcome.Settled, await _settlement.SettleAsync(Trade(1, 99, 55, 2)));
        Assert.Equal(OrderStatus.Filled, (await ReadAsync(sending)).Status);
    }

    [Fact]
    public async Task Later_event_of_a_symbol_does_not_overtake_one_that_waits()
    {
        var open = await AcceptedAsync(7, quantity: 2);
        var inFlight = Order.Place(OrderId.New(), _client, "w", new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 1m, 1), Commission.Standard);
        inFlight.MarkFundsBlocked();
        inFlight.BeginSubmission();
        _orders.Put(inFlight);
        await _inbox.AddAsync(Trade(1, 98, 99, 1)); // moze byc zleceniem w drodze
        await _inbox.AddAsync(Trade(2, 98, 7, 1));  // nasze, ale pozniejsze — musi poczekac w kolejce

        var pass = await _settlement.SettlePendingAsync(10);

        Assert.Equal(0, pass.Settled);
        Assert.Equal(0L, (await ReadAsync(open)).Execution.Quantity);
        await Assert.ThrowsAsync<ArgumentNullException>(() => _settlement.SettleAsync(null!));
    }

    [Fact]
    public async Task Pass_takes_a_limited_number_of_events_from_each_symbol()
    {
        var inFlight = Order.Place(OrderId.New(), _client, "w", new OrderRequest("PKN", OrderSide.Buy, OrderType.Limit, 1m, 1), Commission.Standard);
        inFlight.MarkFundsBlocked();
        inFlight.BeginSubmission();
        _orders.Put(inFlight);
        for (var i = 1; i <= 5; i++)
        {
            await _inbox.AddAsync(Trade(i, 98, 99, 1, symbol: "PKN"));
        }
        await _inbox.AddAsync(Trade(1, 98, 99, 1));

        var pass = await _settlement.SettlePendingAsync(2);

        // Stojaca spolka z kolejka zdarzen nie zabiera calej porcji — CDR i tak jest rozliczone.
        Assert.Equal(1, pass.Settled);
        Assert.Equal("CDR", Assert.Single(_inbox.Settled).Symbol);
    }

    [Fact]
    public async Task Pass_settles_in_order_and_stops_only_the_symbol_that_failed()
    {
        await AcceptedAsync(7, quantity: 2);
        await _inbox.AddAsync(Trade(1, 99, 7, 50));                // wiecej niz zlecenie — blad
        await _inbox.AddAsync(Trade(2, 99, 7, 1));                 // ta sama spolka: czeka za bledem
        await _inbox.AddAsync(Trade(1, 98, 99, 1, symbol: "PKN")); // inna spolka idzie dalej

        var pass = await _settlement.SettlePendingAsync(10);

        Assert.Equal(1, pass.Settled);
        Assert.IsType<ExchangeEventMismatchException>(Assert.Single(pass.Failures).Error);
        Assert.Equal("PKN", Assert.Single(_inbox.Settled).Symbol);
        Assert.Equal(2, (await _inbox.PendingAsync(10)).Count);
    }

    [Fact]
    public async Task Pass_leaves_waiting_and_contended_symbols_for_later()
    {
        await AcceptedAsync(7, quantity: 2);
        var inFlight = Order.Place(OrderId.New(), _client, "w", new OrderRequest("PKN", OrderSide.Buy, OrderType.Limit, 1m, 1), Commission.Standard);
        inFlight.MarkFundsBlocked();
        inFlight.BeginSubmission();
        _orders.Put(inFlight);
        await _inbox.AddAsync(Trade(1, 98, 99, 1, symbol: "PKN")); // moze byc zleceniem w drodze — czeka
        await _inbox.AddAsync(Trade(1, 99, 7, 1));                 // zlecenie wlasnie zmienia reconciler
        await _inbox.AddAsync(Trade(2, 99, 7, 1));                 // czeka za poprzednim
        await _inbox.AddAsync(Trade(1, 1, 2, 1, symbol: "ALE"));   // nie nasze
        _orders.ConflictOnSave = _orders.Saves + 1;

        var pass = await _settlement.SettlePendingAsync(10);

        // Konflikt wersji to nie blad — zwykle ponowienie w nastepnym przebiegu.
        Assert.Equal((1, 0), (pass.Settled, pass.Failures.Count));
        Assert.Equal(["PKN", "CDR", "CDR"], (await _inbox.PendingAsync(10)).Select(e => e.Symbol));
        Assert.Equal(2, (await _settlement.SettlePendingAsync(10)).Settled);
    }
}
