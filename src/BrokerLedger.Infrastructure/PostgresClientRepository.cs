using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BrokerLedger.Infrastructure;

internal sealed class PostgresClientRepository(IDbContextFactory<LedgerDbContext> contexts, TimeProvider clock)
    : IClientRepository
{
    public async Task<bool> TryAddAsync(ClientAccount client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.Clients.Add(new ClientRow
        {
            Id = client.Id.Value,
            Email = client.Email.Value,
            PasswordHash = client.PasswordHash,
            CreatedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        // Dwie rownolegle rejestracje na ten sam adres: rozstrzyga UNIQUE, nie wczesniejszy SELECT.
        catch (DbUpdateException e) when (e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: LedgerDbContext.ClientEmailIndex,
        })
        {
            return false;
        }
    }

    public async Task<ClientAccount?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Email == email.Value, cancellationToken);
        return row is null ? null : new ClientAccount(new ClientId(row.Id), new EmailAddress(row.Email), row.PasswordHash);
    }

    public async Task UpdatePasswordHashAsync(ClientId client, string passwordHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Clients.Where(c => c.Id == client.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.PasswordHash, passwordHash), cancellationToken);
    }
}
