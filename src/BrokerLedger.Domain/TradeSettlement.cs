namespace BrokerLedger.Domain;

public enum SettleOutcome
{
    /// <summary>Zdarzenie dotyczylo zlecen klientow brokera i jest rozliczone.</summary>
    Settled,

    /// <summary>Zlecenia innych uczestnikow gieldy — nic do rozliczenia.</summary>
    NotOurs,

    /// <summary>
    /// Nie wiadomo jeszcze, czy to nasze: na spolce jest zlecenie w drodze, bez numeru gieldy.
    /// Zdarzenie (i kolejne z tej spolki) czeka, az reconciler je wyjasni.
    /// </summary>
    Waiting,
}

/// <summary>Wynik jednego przebiegu po skrzynce: ile rozliczono i co sie nie udalo (do logu).</summary>
public sealed record SettlementPass(int Settled, IReadOnlyList<(ExchangeEvent Event, Exception Error)> Failures);

/// <summary>
/// Rozliczenie zdarzen gieldy: transakcja przenosi gotowke i akcje, koniec zlecenia zwalnia reszte blokady.
/// Kazdy krok jest idempotentny (klucz w ksiedze, ostatnie zdarzenie w zleceniu), wiec zdarzenie mozna
/// rozliczac wielokrotnie — po awarii, po powtorce z Kafki albo rownolegle z reconcilerem.
/// </summary>
public sealed class TradeSettlement(IOrderRepository orders, ILedgerStore ledger, IExchangeInbox inbox, Commission commission)
{
    /// <summary>
    /// Rozlicza zdarzenia ze skrzynki po kolei. Spolka, na ktorej cos czeka albo sie nie udalo, staje do nastepnego
    /// przebiegu — jej pozniejsze zdarzenia nie moga wyprzedzic wczesniejszych. Inne spolki ida dalej
    /// (skrzynka oddaje najwyzej <paramref name="limitPerSymbol"/> zdarzen z kazdej spolki, wiec stojaca ich nie zablokuje).
    /// </summary>
    public async Task<SettlementPass> SettlePendingAsync(int limitPerSymbol, CancellationToken cancellationToken = default)
    {
        var stopped = new HashSet<string>();
        var inFlight = new Dictionary<string, bool>();
        var failures = new List<(ExchangeEvent, Exception)>();
        var settled = 0;
        foreach (var pending in await inbox.PendingAsync(limitPerSymbol, cancellationToken))
        {
            if (stopped.Contains(pending.Symbol))
            {
                continue;
            }
            try
            {
                // Raz na spolke w przebiegu, ale zawsze PO odczycie skrzynki — patrz SettleAsync.
                if (!inFlight.TryGetValue(pending.Symbol, out var waitingPossible))
                {
                    waitingPossible = await orders.HasInFlightAsync(pending.Symbol, cancellationToken);
                    inFlight[pending.Symbol] = waitingPossible;
                }
                if (await SettleAsync(pending, waitingPossible, cancellationToken) == SettleOutcome.Waiting)
                {
                    stopped.Add(pending.Symbol);
                    continue;
                }
                await inbox.MarkSettledAsync(pending, cancellationToken);
                settled++;
            }
            catch (OrderConcurrencyException)
            {
                // Zlecenie zmienial wlasnie ktos inny (reconciler, anulowanie) — wrocimy w nastepnym przebiegu.
                stopped.Add(pending.Symbol);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                stopped.Add(pending.Symbol);
                failures.Add((pending, e));
            }
        }
        return new SettlementPass(settled, failures);
    }

    /// <summary>
    /// Rozlicza jedno zdarzenie, ktore juz jest w skrzynce. Zlecenia "w drodze" sprawdzamy PRZED szukaniem po numerze:
    /// zlecenie przyjete w miedzyczasie i tak sie znajdzie, a zlecenie wyslane pozniej nie moze miec zdarzenia,
    /// ktore juz lezalo w skrzynce. W odwrotnej kolejnosci zlecenie przyjete miedzy jednym a drugim zapytaniem
    /// nie byloby ani znalezione, ani "w drodze" — i jego transakcja przepadlaby jako cudza.
    /// </summary>
    public async Task<SettleOutcome> SettleAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchangeEvent);
        var waitingPossible = await orders.HasInFlightAsync(exchangeEvent.Symbol, cancellationToken);
        return await SettleAsync(exchangeEvent, waitingPossible, cancellationToken);
    }

    private async Task<SettleOutcome> SettleAsync(ExchangeEvent exchangeEvent, bool waitingPossible, CancellationToken cancellationToken)
    {
        var matched = false;
        var unmatched = false;
        foreach (var exchangeOrderId in exchangeEvent.ExchangeOrderIds)
        {
            var order = await FindAsync(exchangeEvent, exchangeOrderId, cancellationToken);
            if (order is null)
            {
                unmatched = true;
                continue;
            }
            await ApplyAsync(order, exchangeEvent, cancellationToken);
            matched = true;
        }
        if (unmatched && waitingPossible)
        {
            return SettleOutcome.Waiting;
        }
        return matched ? SettleOutcome.Settled : SettleOutcome.NotOurs;
    }

    /// <summary>
    /// Zlecenie, ktore rozliczylo dokladnie to zdarzenie jako ostatnie (powtorka po awarii — moze trzeba dokonczyc
    /// zwolnienie), a jesli takiego nie ma — najnowsze otwarte zlecenie z tym numerem, ktore nie rozliczylo jeszcze nic pozniejszego. Najnowsze, bo po starcie gieldy
    /// bez wznowienia numeracja rusza od nowa, a starsze zlecenie z tym numerem pochodzi z poprzedniego przebiegu.
    /// </summary>
    private async Task<Order?> FindAsync(ExchangeEvent exchangeEvent, long exchangeOrderId, CancellationToken cancellationToken)
    {
        var candidates = await orders.ByExchangeOrderIdAsync(exchangeEvent.Symbol, exchangeOrderId, cancellationToken);
        return candidates.FirstOrDefault(o => o.Execution.LastEvent == exchangeEvent.Position)
               ?? candidates.LastOrDefault(o => o.IsOpenOnExchange && !o.Execution.Includes(exchangeEvent.Position));
    }

    private async Task ApplyAsync(Order order, ExchangeEvent exchangeEvent, CancellationToken cancellationToken)
    {
        if (!order.Execution.Includes(exchangeEvent.Position))
        {
            switch (exchangeEvent)
            {
                case ExchangeEvent.Trade trade:
                    await ledger.PostAsync(order.TradeKey(trade.Position), order.TradeEntry(trade, commission), cancellationToken);
                    order.ApplyTrade(trade, commission);
                    break;
                case ExchangeEvent.Cancelled cancelled:
                    order.ApplyCancellation(cancelled);
                    break;
            }
            await OrderSaga.SaveAsync(order, orders, cancellationToken);
        }
        if (order.AwaitsRelease)
        {
            await OrderSaga.ReleaseAsync(order, orders, ledger, cancellationToken);
        }
    }
}
