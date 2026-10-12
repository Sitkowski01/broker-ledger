using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Wiadomosci, ktorych nie da sie zamienic na zdarzenie (zepsuty JSON, wartosci spoza regul brokera). Konsument
/// odklada je tutaj i idzie dalej: jedna zla wiadomosc nie zatrzyma kolejki, ale tez nie przepadnie bez sladu.
/// </summary>
internal sealed class PostgresDeadLetters(IDbContextFactory<LedgerDbContext> contexts, TimeProvider clock)
{
    public const int MaxErrorLength = 1000;

    public async Task AddAsync(string? message, string error, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.DeadLetters.Add(new DeadLetterRow
        {
            Message = message,
            Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error,
            ReceivedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
