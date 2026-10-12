namespace BrokerLedger.Domain;

/// <summary>
/// Droga zlecenia na gielde, krok po kroku. Kazdy krok jest idempotentny (klucz w ksiedze, clientOrderId
/// na gieldzie), wiec <see cref="ProgressAsync"/> mozna wolac wielokrotnie — przy skladaniu i potem
/// z reconcilera — az zlecenie dojdzie do stanu, w ktorym nie ma juz nic do zrobienia.
///
/// Kolejnosc zapisow jest wazna: status zlecenia zapisujemy przed zwolnieniem srodkow, wiec po awarii
/// nigdy nie zwolnimy blokady zlecenia, ktore moglo trafic na gielde.
/// </summary>
public sealed class OrderSaga(IOrderRepository orders, ILedgerStore ledger, IExchange exchange)
{
    public const string InsufficientFunds = "insufficient funds";

    public async Task<Order> ProgressAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Status == OrderStatus.Pending && !order.FundsBlocked)
        {
            try
            {
                await ledger.PostAsync(order.BlockKey, order.BlockEntry(), cancellationToken);
                order.MarkFundsBlocked();
            }
            catch (InsufficientBalanceException)
            {
                order.Reject(InsufficientFunds);
            }
            await SaveAsync(order, cancellationToken);
        }

        if (order.Status is OrderStatus.Pending or OrderStatus.Unknown)
        {
            // Proba zapisana przed wyslaniem. Przy okazji dziala jak dzierzawa: gdy zlecenie chca wyslac naraz
            // dwa procesy (zadanie klienta i reconciler), drugi zapis nie przejdzie i drugi nie wysle.
            order.BeginSubmission();
            await SaveAsync(order, cancellationToken);

            var reply = await exchange.PlaceAsync(order, cancellationToken);
            switch (reply)
            {
                case ExchangeReply.Accepted accepted:
                    order.Accept(accepted.ExchangeOrderId);
                    break;
                case ExchangeReply.Refused refused:
                    order.Reject(refused.Reason);
                    break;
                // Pelna kolejka przy pierwszej probie: zlecenie na pewno nie weszlo. Jesli probowalismy juz wczesniej
                // (timeout, awaria, rozlaczony klient), tamta proba moze jeszcze czekac w kolejce gieldy.
                case ExchangeReply.Busy busy when order.SubmitAttempts == 1:
                    order.Reject(busy.Reason);
                    break;
                default:
                    order.MarkUnknown();
                    break;
            }
            await SaveAsync(order, cancellationToken);
        }

        if (order.AwaitsRelease)
        {
            await ReleaseAsync(order, orders, ledger, cancellationToken);
        }

        return order;
    }

    /// <summary>
    /// Prosba o anulowanie. Zwolnienie reszty przyjdzie ze zdarzenia gieldy — dopiero wtedy wiadomo,
    /// ile zdazylo sie zrealizowac.
    /// </summary>
    public async Task<CancelReply> CancelAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Status != OrderStatus.Accepted)
        {
            return order.Status == OrderStatus.CancelRequested ? CancelReply.Cancelled : CancelReply.NotOpen;
        }
        var reply = await exchange.CancelAsync(order, cancellationToken);
        if (reply == CancelReply.Cancelled)
        {
            order.RequestCancel();
            try
            {
                await SaveAsync(order, cancellationToken);
            }
            catch (OrderConcurrencyException)
            {
                // Gielda juz anulowala, a rozliczenie zdazylo przetworzyc jej zdarzenie (anulowanie albo transakcje)
                // przed naszym zapisem. Anulowanie sie udalo — stan zlecenia ustala teraz rozliczenie.
            }
        }
        return reply;
    }

    /// <summary>
    /// Zwraca klientowi reszte blokady skonczonego zlecenia. Wspolne dla sagi (odmowa) i rozliczenia
    /// (wykonanie, anulowanie) — oba moga dokonczyc zwolnienie, ktore drugie przerwala awaria.
    /// </summary>
    internal static async Task ReleaseAsync(Order order, IOrderRepository orders, ILedgerStore ledger,
        CancellationToken cancellationToken)
    {
        if (order.ReleaseEntry() is { } release)
        {
            await ledger.PostAsync(order.ReleaseKey, release, cancellationToken);
        }
        order.MarkFundsReleased();
        await SaveAsync(order, orders, cancellationToken);
    }

    internal static async Task SaveAsync(Order order, IOrderRepository orders, CancellationToken cancellationToken)
    {
        await orders.SaveAsync(order, cancellationToken);
        order.MarkSaved();
    }

    private Task SaveAsync(Order order, CancellationToken cancellationToken) => SaveAsync(order, orders, cancellationToken);
}
