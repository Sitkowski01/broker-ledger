namespace BrokerLedger.Domain;

/// <summary>Wykonanie zlecenia na gieldzie: ile zostalo do wykonania i ile blokady jeszcze nie zuzyto.</summary>
public sealed partial class Order
{
    /// <summary>Na gieldzie: przyjete i jeszcze nie zakonczone, wiec moga przyjsc transakcje.</summary>
    public bool IsOpenOnExchange => Status is OrderStatus.Accepted or OrderStatus.CancelRequested;

    public long RemainingQuantity => Quantity - Execution.Quantity;

    /// <summary>Ile blokady jeszcze nie zuzyto: przy kupnie w zl, przy sprzedazy w akcjach.</summary>
    public decimal RemainingBlocked => Side == OrderSide.Buy
        ? BlockedAmount - Execution.Value - Execution.Commission
        : BlockedAmount - Execution.Quantity;
}
