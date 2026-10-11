using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Konto w bazie. <see cref="Balance"/> to projekcja — suma zapisow trzymana dla szybkosci.
/// Zmienia ja wylacznie <see cref="PostgresLedgerStore"/> w tej samej transakcji, w ktorej dopisuje zapisy.
/// </summary>
internal sealed class AccountRow
{
    public long Id { get; set; }
    public AccountType Type { get; set; }
    public AssetKind AssetKind { get; set; }
    public required string AssetCode { get; set; }
    public Guid? ClientId { get; set; }
    public PostingSide NormalSide { get; set; }
    public decimal Balance { get; set; }
}

internal sealed class JournalEntryRow
{
    public Guid Id { get; set; }

    /// <summary>Kolejnosc zaksiegowania (identity) — po niej historia sortuje i stronicuje.</summary>
    public long Sequence { get; set; }

    public required string IdempotencyKey { get; set; }
    public required string Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<PostingRow> Postings { get; set; } = [];
}

internal sealed class PostingRow
{
    public long Id { get; set; }
    public Guid EntryId { get; set; }
    public long AccountId { get; set; }
    public AccountRow Account { get; set; } = null!;
    public PostingSide Side { get; set; }
    public decimal Amount { get; set; }
}

internal sealed class ClientRow
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
