using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

internal sealed class PostgresLedgerQueries(IDbContextFactory<LedgerDbContext> contexts) : ILedgerQueries
{
    public async Task<IReadOnlyList<AccountBalance>> ClientBalancesAsync(ClientId client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Accounts.AsNoTracking()
            .Where(a => a.ClientId == client.Value)
            .OrderBy(a => a.AssetKind).ThenBy(a => a.AssetCode).ThenBy(a => a.Type)
            .ToListAsync(cancellationToken);
        return rows.Select(a => new AccountBalance(AccountKey.ToAccountId(a), a.Balance)).ToList();
    }

    public async Task<IReadOnlyList<HistoryEntry>> ClientHistoryAsync(
        ClientId client, int limit, long? before = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, ILedgerQueries.MaxHistoryPage);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var entries = db.JournalEntries.AsNoTracking()
            .Where(e => e.Postings.Any(p => p.Account.ClientId == client.Value));
        if (before is { } cursor)
        {
            entries = entries.Where(e => e.Sequence < cursor);
        }
        return await ToHistoryAsync(client, entries.OrderByDescending(e => e.Sequence).Take(limit), cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryEntry>> ClientShareEntriesAsync(ClientId client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var entries = db.JournalEntries.AsNoTracking()
            .Where(e => e.Postings.Any(p => p.Account.ClientId == client.Value && p.Account.AssetKind == AssetKind.Shares))
            .OrderBy(e => e.Sequence);
        return await ToHistoryAsync(client, entries, cancellationToken);
    }

    /// <summary>Dokumenty z samymi zapisami klienta — cudze konta (bank, depozyt, prowizja) zostaja w bazie.</summary>
    private static async Task<IReadOnlyList<HistoryEntry>> ToHistoryAsync(
        ClientId client, IQueryable<JournalEntryRow> entries, CancellationToken cancellationToken)
    {
        var rows = await entries
            .Select(e => new
            {
                e.Id,
                e.Sequence,
                e.Description,
                e.CreatedAt,
                Postings = e.Postings
                    .Where(p => p.Account.ClientId == client.Value)
                    .OrderBy(p => p.Id)
                    .Select(p => new { p.Account, p.Side, p.Amount })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(e => new HistoryEntry(e.Id, e.Sequence, e.Description, e.CreatedAt,
            e.Postings.Select(p => new Posting(AccountKey.ToAccountId(p.Account), p.Side, p.Amount)).ToList())).ToList();
    }
}
