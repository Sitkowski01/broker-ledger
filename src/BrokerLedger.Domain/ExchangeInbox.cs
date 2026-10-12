namespace BrokerLedger.Domain;

/// <summary>
/// Skrzynka zdarzen z gieldy (wzorzec inbox). Konsument Kafki tylko je tu odklada i potwierdza odczyt;
/// rozliczenie idzie osobno, bo zdarzenie moze przyjsc, zanim broker pozna numer swojego zlecenia.
/// </summary>
public interface IExchangeInbox
{
    /// <summary>Odklada zdarzenie. False: to samo zdarzenie juz jest (Kafka dostarcza co najmniej raz).</summary>
    Task<bool> AddAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nierozliczone zdarzenia w kolejnosci, w jakiej przyszly — w obrebie spolki to kolejnosc gieldy. Najwyzej
    /// <paramref name="limitPerSymbol"/> pierwszych z kazdej spolki: spolka, ktora stoi, nie zajmie calej porcji.
    /// </summary>
    Task<IReadOnlyList<ExchangeEvent>> PendingAsync(int limitPerSymbol, CancellationToken cancellationToken = default);

    Task MarkSettledAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default);
}
