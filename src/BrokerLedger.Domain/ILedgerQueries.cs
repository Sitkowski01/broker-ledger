namespace BrokerLedger.Domain;

public sealed record AccountBalance(AccountId Account, decimal Balance);

/// <summary>
/// Dokument z punktu widzenia klienta: zapisy na jego kontach i na kontach brokera (bank, depozyt, prowizje) —
/// bez kont innych klientow. <see cref="Order"/> to zlecenie, ktorego dotyczy dokument. <see cref="Sequence"/> to kolejnosc
/// zaksiegowania — po niej sortujemy i stronicujemy, bo czas i UUID v7 w tej samej milisekundzie jej nie ustalaja.
/// <see cref="Key"/> to klucz idempotencji dokumentu — po nim widac, z jakiego zdarzenia powstal.
/// </summary>
public sealed record HistoryEntry(
    Guid Id, long Sequence, string Description, DateTimeOffset CreatedAt, IReadOnlyList<Posting> Postings, OrderId? Order = null,
    string? Key = null);

/// <summary>Odczyty dla klienta. Osobno od <see cref="ILedgerStore"/>: zapis i odczyt maja rozne potrzeby.</summary>
public interface ILedgerQueries
{
    public const int MaxHistoryPage = 100;

    Task<IReadOnlyList<AccountBalance>> ClientBalancesAsync(ClientId client, CancellationToken cancellationToken = default);

    /// <summary>
    /// Od najnowszych; <paramref name="before"/> to <see cref="HistoryEntry.Sequence"/> ostatniego dokumentu z poprzedniej strony.
    /// <paramref name="descriptions"/> zaweza do dokumentow danego rodzaju (np. tylko wplaty i wyplaty); null = wszystkie.
    /// </summary>
    Task<IReadOnlyList<HistoryEntry>> ClientHistoryAsync(
        ClientId client, int limit, long? before = null, IReadOnlyCollection<string>? descriptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wszystkie dokumenty, ktore dotknely akcji klienta, od najstarszych — z kompletem jego zapisow
    /// (takze gotowkowych), bo z nich <see cref="Portfolio"/> liczy koszt i zysk.
    /// </summary>
    Task<IReadOnlyList<HistoryEntry>> ClientShareEntriesAsync(ClientId client, CancellationToken cancellationToken = default);
}
