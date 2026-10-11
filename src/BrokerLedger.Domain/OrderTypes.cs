namespace BrokerLedger.Domain;

public enum OrderSide
{
    Buy,
    Sell,
}

public enum OrderType
{
    Limit,
    Market,
}

public enum OrderStatus
{
    /// <summary>Zapisane; srodki byc moze zablokowane, gielda jeszcze nie odpowiedziala.</summary>
    Pending,

    /// <summary>Gielda nie odpowiedziala na czas — nie wiadomo, czy zlecenie weszlo. Do wyjasnienia.</summary>
    Unknown,

    /// <summary>Gielda przyjela zlecenie (ma numer). Rozliczenia przychodza z jej zdarzen.</summary>
    Accepted,

    /// <summary>Klient poprosil o anulowanie, gielda je przyjela; reszte zwolni zdarzenie z gieldy.</summary>
    CancelRequested,

    /// <summary>Zlecenie nie weszlo na gielde (brak srodkow, odmowa gieldy). Blokada do zwolnienia.</summary>
    Rejected,
}

public sealed record OrderId(Guid Value)
{
    public Guid Value { get; } = Value == Guid.Empty ? throw new ArgumentException("order id must not be empty", nameof(Value)) : Value;

    public static OrderId New() => new(Guid.CreateVersion7());

    /// <summary>Identyfikator dla gieldy (clientOrderId): 32 znaki szesnastkowe.</summary>
    public string ForExchange => Value.ToString("N");

    public override string ToString() => Value.ToString();
}

/// <summary>Parametry zlecenia od klienta — po nich poznajemy, ze powtorka zadania to ta sama prosba.</summary>
public sealed record OrderRequest(string Symbol, OrderSide Side, OrderType Type, decimal? LimitPrice, long Quantity);

public sealed class InvalidOrderTransitionException(OrderId order, OrderStatus status, string action)
    : InvalidOperationException($"order {order} in status {status} cannot {action}")
{
    public OrderStatus Status { get; } = status;
}
