namespace BrokerLedger.Domain;

/// <summary>Ten sam klucz idempotencji przyszedl z innym dokumentem — to blad wywolujacego, nie powtorka.</summary>
public sealed class IdempotencyConflictException(string idempotencyKey)
    : Exception($"idempotency key '{idempotencyKey}' was already used for a different entry")
{
    public string IdempotencyKey { get; } = idempotencyKey;
}
