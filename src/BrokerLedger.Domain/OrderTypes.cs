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

    /// <summary>Gielda przyjela zlecenie (ma numer). Transakcje przychodza z jej zdarzen, moze byc juz czesciowo wykonane.</summary>
    Accepted,

    /// <summary>Klient poprosil o anulowanie, gielda je przyjela; reszte zwolni zdarzenie z gieldy.</summary>
    CancelRequested,

    /// <summary>Zlecenie nie weszlo na gielde (brak srodkow, odmowa gieldy). Blokada do zwolnienia.</summary>
    Rejected,

    /// <summary>Wykonane w calosci. Niewykorzystana czesc blokady (kupno ponizej limitu) wraca do klienta.</summary>
    Filled,

    /// <summary>
    /// Gielda zdjela reszte zlecenia (anulowanie, PKC bez ofert albo start gieldy od pustych arkuszy).
    /// Reszta blokady wraca do klienta.
    /// </summary>
    Cancelled,
}

/// <summary>
/// Ile zlecenia juz wykonano: akcje, wartosc i pobrana prowizja (zl), plus ostatnie rozliczone zdarzenie gieldy —
/// po nim poznajemy powtorke tego samego zdarzenia.
/// </summary>
public sealed record OrderExecution(long Quantity, decimal Value, decimal Commission, EventPosition? LastEvent)
{
    public static readonly OrderExecution None = new(0, 0m, 0m, null);

    public bool Includes(EventPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return LastEvent is not null && LastEvent.IsAtOrAfter(position);
    }
}

public sealed record OrderId(Guid Value)
{
    public Guid Value { get; } = Value == Guid.Empty ? throw new ArgumentException("order id must not be empty", nameof(Value)) : Value;

    public static OrderId New() => new(Guid.CreateVersion7());

    /// <summary>Identyfikator dla gieldy (clientOrderId): 32 znaki szesnastkowe.</summary>
    public string ForExchange => Value.ToString("N");

    /// <summary>
    /// Chwila zlozenia zlecenia. UUID v7 zaczyna sie od 48 bitow czasu uniksowego w milisekundach,
    /// a identyfikator powstaje przy skladaniu — wiec czas jest w nim zapisany i nie trzeba osobnej kolumny.
    /// Dla starszych identyfikatorow (nie v7) czasu nie znamy.
    /// </summary>
    public DateTimeOffset? CreatedAt
    {
        get
        {
            if (Value.Version != 7)
            {
                return null;
            }
            var bytes = Value.ToByteArray(bigEndian: true);
            var milliseconds = 0L;
            for (var i = 0; i < 6; i++)
            {
                milliseconds = (milliseconds << 8) | bytes[i];
            }
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
    }

    public override string ToString() => Value.ToString();
}

/// <summary>Parametry zlecenia od klienta — po nich poznajemy, ze powtorka zadania to ta sama prosba.</summary>
public sealed record OrderRequest(string Symbol, OrderSide Side, OrderType Type, decimal? LimitPrice, long Quantity);

public sealed class InvalidOrderTransitionException(OrderId order, OrderStatus status, string action)
    : InvalidOperationException($"order {order} in status {status} cannot {action}")
{
    public OrderStatus Status { get; } = status;
}
