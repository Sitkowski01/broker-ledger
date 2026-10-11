using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Ksiega w PostgreSQL. Zapisy tylko sie dopisuje (pilnuje tego trigger), a salda zmieniaja sie w tej samej
/// transakcji na zablokowanych wierszach — bez wyscigu "sprawdz, potem zapisz".
/// </summary>
internal sealed class PostgresLedgerStore(IDbContextFactory<LedgerDbContext> contexts, TimeProvider clock) : ILedgerStore
{
    public async Task<PostOutcome> PostAsync(string idempotencyKey, JournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > ILedgerStore.MaxIdempotencyKeyLength)
        {
            throw new ArgumentException($"idempotency key must be at most {ILedgerStore.MaxIdempotencyKeyLength} characters", nameof(idempotencyKey));
        }
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            await PostNewAsync(idempotencyKey, entry, cancellationToken);
            return PostOutcome.Posted;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: LedgerDbContext.IdempotencyIndex,
        })
        {
            var existing = await LoadAsync(idempotencyKey, cancellationToken);
            return existing.HasSameContentAs(entry)
                ? PostOutcome.AlreadyPosted
                : throw new IdempotencyConflictException(idempotencyKey);
        }
    }

    public async Task<decimal> BalanceAsync(AccountId account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await AccountKey.Of(account).Find(db.Accounts)
            .Select(a => (decimal?)a.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0m;
    }

    private async Task PostNewAsync(string idempotencyKey, JournalEntry entry, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // Konta zakladamy poza transakcja: puste konto niczego nie psuje, a krotkie osobne
        // INSERT-y nie trzymaja blokad do konca ksiegowania.
        var accountIds = new Dictionary<AccountId, long>();
        foreach (var posting in entry.Postings)
        {
            accountIds[posting.Account] = await EnsureAccountAsync(db, posting.Account, cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Najpierw dokument. Rownolegla powtorka z tym samym kluczem czeka tu na pierwsza
        // i dostaje naruszenie UNIQUE, zanim dotknie sald — nie zobaczy np. "brak srodkow".
        db.JournalEntries.Add(new JournalEntryRow
        {
            Id = Guid.CreateVersion7(),
            IdempotencyKey = idempotencyKey,
            Description = entry.Description,
            CreatedAt = clock.GetUtcNow(),
            Postings = entry.Postings
                .Select(p => new PostingRow { AccountId = accountIds[p.Account], Side = p.Side, Amount = p.Amount })
                .ToList(),
        });
        await db.SaveChangesAsync(cancellationToken);

        // Wiersze kont blokujemy zawsze w kolejnosci id: dwa dokumenty na tych samych kontach czekaja
        // na siebie w tej samej kolejnosci, wiec nie moga sie zakleszczyc. Do commita nikt inny nie zmieni
        // tych sald — sprawdzenie i zapis widza to samo. NO KEY UPDATE, a nie UPDATE: wstawione wyzej
        // zapisy trzymaja przez klucz obcy KEY SHARE na tych kontach, a FOR UPDATE by z nim kolidowal.
        var balances = new Dictionary<AccountId, decimal>();
        // Stryker disable once Linq : kolejnosc malejaca tez jest stala, wiec tez chroni przed zakleszczeniem
        foreach (var (account, id) in accountIds.OrderBy(a => a.Value))
        {
            balances[account] = await db.Database
                .SqlQuery<decimal>($"""SELECT balance AS "Value" FROM accounts WHERE id = {id} FOR NO KEY UPDATE""")
                .SingleAsync(cancellationToken);
        }

        // Ta sama regula co w ksiedze w pamieci — i to samo konto w wyjatku.
        entry.EnsureNoAccountGoesNegative(account => balances[account]);

        foreach (var posting in entry.Postings)
        {
            var id = accountIds[posting.Account];
            var balance = balances[posting.Account] + posting.BalanceChange;
            await db.Accounts.Where(a => a.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, balance), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<long> EnsureAccountAsync(LedgerDbContext db, AccountId account, CancellationToken cancellationToken)
    {
        var key = AccountKey.Of(account);
        // Zwykle konto juz jest — wtedy jedno zapytanie. INSERT tylko dla nowego konta.
        var existing = await key.Find(db.Accounts).Select(a => (long?)a.Id).SingleOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            return id;
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO accounts (type, asset_kind, asset_code, client_id, normal_side, balance)
            VALUES ({key.Type.ToString()}, {key.AssetKind.ToString()}, {key.AssetCode}, {key.ClientId}, {key.NormalSide.ToString()}, 0)
            ON CONFLICT (type, asset_kind, asset_code, client_id) DO NOTHING
            """, cancellationToken);
        return await key.Find(db.Accounts).Select(a => a.Id).SingleAsync(cancellationToken);
    }

    private async Task<JournalEntry> LoadAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.JournalEntries.AsNoTracking()
            .Include(e => e.Postings).ThenInclude(p => p.Account)
            .SingleAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken);
        return new JournalEntry(row.Description,
            row.Postings.Select(p => new Posting(AccountKey.ToAccountId(p.Account), p.Side, p.Amount)));
    }
}
