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

internal sealed class OrderRow
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public required string ClientKey { get; set; }
    public required string Symbol { get; set; }
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal? LimitPrice { get; set; }
    public long Quantity { get; set; }
    public decimal BlockedAmount { get; set; }
    public OrderStatus Status { get; set; }
    public long? ExchangeOrderId { get; set; }
    public long? ExchangeRunId { get; set; }
    public string? RejectionReason { get; set; }
    public bool FundsBlocked { get; set; }
    public bool FundsReleased { get; set; }
    public int SubmitAttempts { get; set; }
    public long FilledQuantity { get; set; }
    public decimal FilledValue { get; set; }
    public decimal FilledCommission { get; set; }
    public long? LastEventRunId { get; set; }
    public long? LastEventSequence { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Zdarzenie gieldy w skrzynce. Kolumny jednego z typow (TRADE / CANCELLED) — drugiego zostaja puste.</summary>
internal sealed class InboxRow
{
    /// <summary>Kolejnosc przyjscia (identity) — w niej rozliczamy.</summary>
    public long Id { get; set; }

    public long RunId { get; set; }
    public required string Symbol { get; set; }
    public long Sequence { get; set; }
    public required string Type { get; set; }
    public long? MakerOrderId { get; set; }
    public long? TakerOrderId { get; set; }
    public OrderSide? TakerSide { get; set; }
    public decimal? Price { get; set; }
    public long? OrderId { get; set; }
    public string? Reason { get; set; }
    public long Quantity { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Czas zdarzenia na gieldzie; gdy gielda go nie podala — czas odbioru.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? SettledAt { get; set; }
}

/// <summary>Wiadomosc z Kafki, ktorej broker nie umial przyjac — zostaje do wyjasnienia, zamiast zniknac.</summary>
internal sealed class DeadLetterRow
{
    public long Id { get; set; }
    public string? Message { get; set; }
    public required string Error { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
