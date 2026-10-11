namespace BrokerLedger.Domain;

public sealed record AccountBalance(AccountId Account, decimal Balance);

/// <summary>
/// Dokument z punktu widzenia klienta: tylko zapisy na jego kontach. <see cref="Sequence"/> to kolejnosc
/// zaksiegowania — po niej sortujemy i stronicujemy, bo czas i UUID v7 w tej samej milisekundzie jej nie ustalaja.
/// </summary>
public sealed record HistoryEntry(Guid Id, long Sequence, string Description, DateTimeOffset CreatedAt, IReadOnlyList<Posting> Postings);

/// <summary>Odczyty dla klienta. Osobno od <see cref="ILedgerStore"/>: zapis i odczyt maja rozne potrzeby.</summary>
public interface ILedgerQueries
{
    public const int MaxHistoryPage = 100;

    Task<IReadOnlyList<AccountBalance>> ClientBalancesAsync(ClientId client, CancellationToken cancellationToken = default);

    /// <summary>Od najnowszych; <paramref name="before"/> to <see cref="HistoryEntry.Sequence"/> ostatniego dokumentu z poprzedniej strony.</summary>
    Task<IReadOnlyList<HistoryEntry>> ClientHistoryAsync(
        ClientId client, int limit, long? before = null, CancellationToken cancellationToken = default);
}
