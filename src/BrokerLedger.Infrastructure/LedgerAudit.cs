using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Kontrola ksiegi prosto w SQL, niezaleznie od kodu, ktory ksieguje. Pusta lista = wszystko sie zgadza.
/// </summary>
internal sealed class LedgerAudit(IDbContextFactory<LedgerDbContext> contexts)
{
    /// <summary>Konta, na ktorych saldo (projekcja) rozjechalo sie z suma zapisow (zrodlem prawdy).</summary>
    public async Task<IReadOnlyList<BalanceMismatch>> BalanceMismatchesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Database.SqlQuery<BalanceMismatch>($"""
            WITH recomputed AS (
                SELECT a.id AS account_id, a.balance,
                       COALESCE(SUM(CASE WHEN p.side = a.normal_side THEN p.amount ELSE -p.amount END), 0) AS from_postings
                FROM accounts a
                LEFT JOIN postings p ON p.account_id = a.id
                GROUP BY a.id, a.balance
            )
            SELECT account_id, balance, from_postings FROM recomputed WHERE balance <> from_postings
            """).ToListAsync(cancellationToken);
    }

    /// <summary>Bilans probny: aktywa, w ktorych suma sald po Wn nie rowna sie sumie sald po Ma.</summary>
    public async Task<IReadOnlyList<TrialBalanceGap>> TrialBalanceGapsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Database.SqlQuery<TrialBalanceGap>($"""
            WITH sides AS (
                SELECT asset_kind, asset_code,
                       COALESCE(SUM(balance) FILTER (WHERE normal_side = 'Debit'), 0) AS debit_side,
                       COALESCE(SUM(balance) FILTER (WHERE normal_side = 'Credit'), 0) AS credit_side
                FROM accounts
                GROUP BY asset_kind, asset_code
            )
            SELECT asset_kind, asset_code, debit_side, credit_side FROM sides WHERE debit_side <> credit_side
            """).ToListAsync(cancellationToken);
    }
}

internal sealed record BalanceMismatch(long AccountId, decimal Balance, decimal FromPostings);

internal sealed record TrialBalanceGap(string AssetKind, string AssetCode, decimal DebitSide, decimal CreditSide);
