namespace BrokerLedger.Domain;

public enum ClientEventKind
{
    /// <summary>Zlecenie klienta zmienilo stan (saga, reconciler, rozliczenie, anulowanie).</summary>
    Order,

    /// <summary>W ksiedze przybyl dokument z kontem klienta — zmienily sie salda.</summary>
    Ledger,

    /// <summary>
    /// Nasluch zerwal sie i wrocil — cos moglo przepasc w przerwie. Klient powinien przeczytac wszystko od nowa.
    /// </summary>
    Resync,
}

/// <summary>
/// Sygnal "u klienta cos sie zmienilo". Celowo bez kwot i sald: klient po sygnale pyta API o aktualny stan,
/// wiec zgubiony albo podwojny sygnal niczego nie psuje — prawda zostaje w bazie.
/// </summary>
public sealed record ClientEvent(ClientId Client, ClientEventKind Kind, OrderId? Order = null, OrderStatus? Status = null);

/// <summary>Strumien zdarzen jednego klienta od chwili subskrypcji (do Server-Sent Events).</summary>
public interface IClientEvents
{
    IAsyncEnumerable<ClientEvent> SubscribeAsync(ClientId client, CancellationToken cancellationToken);
}
