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
        ClientId client, int limit, long? before = null, IReadOnlyCollection<string>? descriptions = null,
        CancellationToken cancellationToken = default)
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
        if (descriptions is not null)
        {
            entries = entries.Where(e => descriptions.Contains(e.Description));
        }
        return await ToHistoryAsync(client, entries.OrderByDescending(e => e.Sequence).Take(limit), withBroker: true, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryEntry>> ClientShareEntriesAsync(ClientId client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var entries = db.JournalEntries.AsNoTracking()
            .Where(e => e.Postings.Any(p => p.Account.ClientId == client.Value && p.Account.AssetKind == AssetKind.Shares))
            .OrderBy(e => e.Sequence);
        return await ToHistoryAsync(client, entries, withBroker: false, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryEntry>> ClientEntriesAsync(ClientId client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var entries = db.JournalEntries.AsNoTracking()
            .Where(e => e.Postings.Any(p => p.Account.ClientId == client.Value))
            .OrderBy(e => e.Sequence);
        return await ToHistoryAsync(client, entries, withBroker: false, cancellationToken);
    }

    /// <summary>
    /// Dokumenty z zapisami klienta i — gdy <paramref name="withBroker"/> — kontami brokera (bank, depozyt, prowizja),
    /// zeby klient widzial druga strone operacji. Konta innych klientow nigdy nie wychodza z bazy.
    /// </summary>
    private static async Task<IReadOnlyList<HistoryEntry>> ToHistoryAsync(
        ClientId client, IQueryable<JournalEntryRow> entries, bool withBroker, CancellationToken cancellationToken)
    {
        var rows = await entries
            .Select(e => new
            {
                e.Id,
                e.Sequence,
                e.Description,
                e.CreatedAt,
                e.IdempotencyKey,
                Postings = e.Postings
                    .Where(p => p.Account.ClientId == client.Value || (withBroker && p.Account.ClientId == null))
                    .OrderBy(p => p.Id)
                    .Select(p => new { p.Account, p.Side, p.Amount })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(e => new HistoryEntry(e.Id, e.Sequence, e.Description, e.CreatedAt,
            e.Postings.Select(p => new Posting(AccountKey.ToAccountId(p.Account), p.Side, p.Amount)).ToList(),
            Order.OfLedgerKey(e.IdempotencyKey), e.IdempotencyKey)).ToList();
    }
}
