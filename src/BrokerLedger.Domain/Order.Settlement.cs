namespace BrokerLedger.Domain;

/// <summary>
/// Rozliczenie zdarzen gieldy na zleceniu. Dokument liczymy ze stanu sprzed zdarzenia, a stan zmieniamy
/// dopiero po zaksiegowaniu — wiec po awarii miedzy jednym a drugim powtorka daje identyczny dokument
/// (ksiega odpowie "juz bylo"), a zdarzenie juz zapisane w zleceniu rozpoznaje <see cref="OrderExecution.Includes"/>.
/// </summary>
public sealed partial class Order
{
    /// <summary>Na gieldzie: przyjete i jeszcze nie zakonczone, wiec moga przyjsc transakcje.</summary>
    public bool IsOpenOnExchange => Status is OrderStatus.Accepted or OrderStatus.CancelRequested;

    public long RemainingQuantity => Quantity - Execution.Quantity;

    /// <summary>Ile blokady jeszcze nie zuzyto: przy kupnie w zl, przy sprzedazy w akcjach.</summary>
    public decimal RemainingBlocked => Side == OrderSide.Buy
        ? BlockedAmount - Execution.Value - Execution.Commission
        : BlockedAmount - Execution.Quantity;

    public string TradeKey(EventPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return $"order:{Id}:trade:{position}";
    }

    /// <summary>Dokument ksiegowy transakcji: kupno albo sprzedaz z prowizja.</summary>
    public JournalEntry TradeEntry(ExchangeEvent.Trade trade, Commission commission)
    {
        var fee = CheckTrade(trade, commission);
        return Side == OrderSide.Buy
            ? JournalEntries.Purchase(Client, Symbol, trade.Quantity, trade.Value, fee)
            : JournalEntries.Sale(Client, Symbol, trade.Quantity, trade.Value, fee);
    }

    /// <summary>Zapisuje transakcje w zleceniu (po zaksiegowaniu <see cref="TradeEntry"/>).</summary>
    public void ApplyTrade(ExchangeEvent.Trade trade, Commission commission)
    {
        var fee = CheckTrade(trade, commission);
        Execution = new OrderExecution(Execution.Quantity + trade.Quantity, Execution.Value + trade.Value,
            Execution.Commission + fee, trade.Position);
        if (RemainingQuantity == 0)
        {
            Status = OrderStatus.Filled;
        }
    }

    /// <summary>Gielda zdjela reszte zlecenia — to, co zostalo z blokady, wroci do klienta.</summary>
    public void ApplyCancellation(ExchangeEvent.Cancelled cancelled)
    {
        ArgumentNullException.ThrowIfNull(cancelled);
        CheckEvent(cancelled, "settle a cancellation");
        if (cancelled.Quantity != RemainingQuantity)
        {
            throw Mismatch(cancelled, $"exchange cancelled {cancelled.Quantity}, but {RemainingQuantity} was open");
        }
        Execution = Execution with { LastEvent = cancelled.Position };
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Sprawdza, ze transakcja pasuje do zlecenia, i zwraca prowizje za nia.</summary>
    private decimal CheckTrade(ExchangeEvent.Trade trade, Commission commission)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(commission);
        CheckEvent(trade, "settle a trade");
        if (trade.SideOf(ExchangeOrderId!.Value) != Side)
        {
            throw Mismatch(trade, $"the order is on the {Side} side");
        }
        if (trade.Quantity > RemainingQuantity)
        {
            throw Mismatch(trade, $"traded {trade.Quantity}, but only {RemainingQuantity} was open");
        }
        if (Side == OrderSide.Buy && trade.Price > LimitPrice)
        {
            throw Mismatch(trade, $"bought at {trade.Price}, above the limit {LimitPrice}");
        }
        if (Side == OrderSide.Sell && trade.Price < LimitPrice)
        {
            throw Mismatch(trade, $"sold at {trade.Price}, below the limit {LimitPrice}");
        }
        var charged = commission.ForFilled(Money.Pln(Execution.Value + trade.Value)).Amount;
        return charged - Execution.Commission;
    }

    private void CheckEvent(ExchangeEvent e, string action)
    {
        Require(IsOpenOnExchange && !Execution.Includes(e.Position), action);
        if (e.Symbol != Symbol || !e.ExchangeOrderIds.Contains(ExchangeOrderId!.Value))
        {
            throw Mismatch(e, $"it is about {e.Symbol} orders {string.Join(", ", e.ExchangeOrderIds)}");
        }
    }

    private ExchangeEventMismatchException Mismatch(ExchangeEvent e, string problem) => new(Id, e.Position, problem);
}
