namespace BrokerLedger.Domain;

public interface IOrderRepository
{
    /// <summary>
    /// Zapisuje nowe zlecenie albo — gdy klient juz zlozyl zlecenie z tym kluczem — zwraca tamto.
    /// </summary>
    Task<Order> AddOrGetAsync(Order order, CancellationToken cancellationToken = default);

    Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ClientOrdersAsync(ClientId client, int limit, CancellationToken cancellationToken = default);

    /// <summary>Zapis zmiany stanu, tylko jesli nikt inny nie zmienil zlecenia od odczytu.</summary>
    /// <exception cref="OrderConcurrencyException">Ktos byl szybszy — trzeba odczytac zlecenie od nowa.</exception>
    Task SaveAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zlecenia do dokonczenia przez reconcilera (<see cref="Order.NeedsAttention"/>), najstarsze najpierw.
    /// Tylko zmienione przed <paramref name="updatedBefore"/> — swiezymi zajmuje sie jeszcze zadanie klienta.
    /// </summary>
    Task<IReadOnlyList<Order>> NeedingAttentionAsync(DateTimeOffset updatedBefore, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zlecenia z tym numerem gieldy. Zwykle jedno; wiecej, gdy gielda zaczela nowy przebieg i numeracja
    /// ruszyla od poczatku. Od najstarszego.
    /// </summary>
    Task<IReadOnlyList<Order>> ByExchangeOrderIdAsync(string symbol, long exchangeOrderId, CancellationToken cancellationToken = default);

    /// <summary>Czy na tej spolce jest zlecenie <see cref="Order.IsInFlight"/>.</summary>
    Task<bool> HasInFlightAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class OrderConcurrencyException(OrderId order)
    : Exception($"order {order} was changed by someone else")
{
    public OrderId Order { get; } = order;
}

/// <summary>Odpowiedz gieldy na zlecenie.</summary>
public abstract record ExchangeReply
{
    private ExchangeReply()
    {
    }

    public sealed record Accepted(long ExchangeOrderId) : ExchangeReply;

    /// <summary>Gielda na pewno nie przyjela zlecenia (zla tresc, nieznana spolka).</summary>
    public sealed record Refused(string Reason) : ExchangeReply;

    /// <summary>
    /// Gielda zajeta (pelna kolejka) — tym razem zlecenie nie weszlo. Ale jesli wczesniejsza proba skonczyla
    /// sie timeoutem, mogla jeszcze czekac w kolejce i wykonac sie pozniej, wiec to nie zawsze jest odmowa.
    /// </summary>
    public sealed record Busy(string Reason) : ExchangeReply;

    /// <summary>Brak odpowiedzi (timeout, blad sieci) — nie wiadomo, czy zlecenie weszlo.</summary>
    public sealed record NoAnswer(string Reason) : ExchangeReply;
}

public enum CancelReply
{
    Cancelled,

    /// <summary>Zlecenia nie ma juz w arkuszu — zrealizowane albo anulowane wczesniej.</summary>
    NotOpen,

    NoAnswer,
}

/// <summary>Gielda (Parkiet) widziana przez brokera. Zlecenie idzie z clientOrderId = id zlecenia brokera.</summary>
public interface IExchange
{
    Task<ExchangeReply> PlaceAsync(Order order, CancellationToken cancellationToken = default);

    Task<CancelReply> CancelAsync(Order order, CancellationToken cancellationToken = default);
}
