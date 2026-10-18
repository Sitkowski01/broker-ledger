using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Skrzynka zdarzen gieldy w PostgreSQL. Powtorke rozpoznaje UNIQUE (run_id, symbol, sequence) —
/// dwa konsumenty, ktore naraz dostana to samo zdarzenie, nie wstawia go dwa razy.
/// </summary>
internal sealed class PostgresExchangeInbox(IDbContextFactory<LedgerDbContext> contexts, TimeProvider clock) : IExchangeInbox
{
    public const string Trade = "TRADE";
    public const string Cancelled = "CANCELLED";

    public async Task<bool> AddAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default)
    {
        var row = ToRow(exchangeEvent);
        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO exchange_inbox (run_id, symbol, sequence, type, maker_order_id, taker_order_id, taker_side,
                                        price, order_id, reason, quantity, received_at, occurred_at)
            VALUES ({row.RunId}, {row.Symbol}, {row.Sequence}, {row.Type}, {row.MakerOrderId}, {row.TakerOrderId},
                    {row.TakerSide?.ToString()}, {row.Price}, {row.OrderId}, {row.Reason}, {row.Quantity}, {now}, {exchangeEvent.OccurredAt ?? now})
            ON CONFLICT (run_id, symbol, sequence) DO NOTHING
            """, cancellationToken);
        return inserted == 1;
    }

    public async Task<IReadOnlyList<ExchangeEvent>> PendingAsync(int limitPerSymbol, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limitPerSymbol, 1);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // Pierwsze N z kazdej spolki (row_number w partycji spolki) — zwykly LIMIT oddalby cala porcje spolce,
        // ktora stoi i ma setki zdarzen w kolejce, a reszta spolek nie ruszylaby wcale.
        var rows = await db.Inbox.FromSqlInterpolated($"""
                SELECT * FROM exchange_inbox WHERE id IN (
                    SELECT id FROM (
                        SELECT id, row_number() OVER (PARTITION BY symbol ORDER BY id) AS position
                        FROM exchange_inbox WHERE settled_at IS NULL) pending
                    WHERE position <= {limitPerSymbol})
                """)
            .AsNoTracking()
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToEvent).ToList();
    }

    public async Task MarkSettledAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchangeEvent);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Inbox
            .Where(e => e.RunId == exchangeEvent.Position.RunId && e.Symbol == exchangeEvent.Symbol
                        && e.Sequence == exchangeEvent.Position.Sequence && e.SettledAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.SettledAt, clock.GetUtcNow()), cancellationToken);
    }

    private static InboxRow ToRow(ExchangeEvent exchangeEvent)
    {
        ArgumentNullException.ThrowIfNull(exchangeEvent);
        var row = new InboxRow
        {
            RunId = exchangeEvent.Position.RunId,
            Symbol = exchangeEvent.Symbol,
            Sequence = exchangeEvent.Position.Sequence,
            Type = exchangeEvent is ExchangeEvent.Trade ? Trade : Cancelled,
        };
        switch (exchangeEvent)
        {
            case ExchangeEvent.Trade trade:
                (row.MakerOrderId, row.TakerOrderId, row.TakerSide) = (trade.MakerOrderId, trade.TakerOrderId, trade.TakerSide);
                (row.Price, row.Quantity) = (trade.Price, trade.Quantity);
                break;
            case ExchangeEvent.Cancelled cancelled:
                (row.OrderId, row.Reason, row.Quantity) = (cancelled.OrderId, cancelled.Reason, cancelled.Quantity);
                break;
        }
        return row;
    }

    private static ExchangeEvent ToEvent(InboxRow row)
    {
        var position = new EventPosition(row.RunId, row.Sequence);
        ExchangeEvent restored = row.Type == Trade
            ? new ExchangeEvent.Trade(row.Symbol, position, row.MakerOrderId!.Value, row.TakerOrderId!.Value,
                row.TakerSide!.Value, row.Price!.Value, row.Quantity)
            : new ExchangeEvent.Cancelled(row.Symbol, position, row.OrderId!.Value, row.Quantity, row.Reason!);
        return restored with { OccurredAt = row.OccurredAt };
    }
}
