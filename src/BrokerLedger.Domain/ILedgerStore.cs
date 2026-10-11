namespace BrokerLedger.Domain;

public enum PostOutcome
{
    Posted,

    /// <summary>Dokument z tym kluczem juz jest w ksiedze — powtorka niczego nie zmienila.</summary>
    AlreadyPosted,
}

/// <summary>Trwala ksiega. Reguly te same co w <see cref="Ledger"/>, plus odpornosc na powtorki i wspolbieznosc.</summary>
public interface ILedgerStore
{
    public const int MaxIdempotencyKeyLength = 200;

    /// <summary>
    /// Ksieguje dokument w calosci albo wcale. Klucz idempotencji nadaje wywolujacy (np. id przelewu
    /// albo zdarzenia z gieldy): ponowienie z tym samym kluczem i tym samym dokumentem zwraca
    /// <see cref="PostOutcome.AlreadyPosted"/>, z innym dokumentem — <see cref="IdempotencyConflictException"/>.
    /// </summary>
    /// <exception cref="InsufficientBalanceException">Konto zeszloby ponizej zera.</exception>
    Task<PostOutcome> PostAsync(string idempotencyKey, JournalEntry entry, CancellationToken cancellationToken = default);

    Task<decimal> BalanceAsync(AccountId account, CancellationToken cancellationToken = default);
}
