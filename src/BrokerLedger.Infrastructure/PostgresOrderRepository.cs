using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Zlecenia w PostgreSQL. Zmiana stanu to warunkowy UPDATE po wersji (blokada optymistyczna): jesli zadanie
/// klienta i reconciler wezma to samo zlecenie, drugi zapis sie nie uda, zamiast nadpisac pierwszy.
/// </summary>
internal sealed class PostgresOrderRepository(IDbContextFactory<LedgerDbContext> contexts, TimeProvider clock)
    : IOrderRepository
{
    public async Task<Order> AddOrGetAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        // ON CONFLICT zamiast "sprawdz, potem wstaw": dwie rownolegle powtorki zadania nie wstawia dwoch zlecen.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO orders (id, client_id, client_key, symbol, side, type, limit_price, quantity, blocked_amount,
                                status, funds_blocked, funds_released, submit_attempts, filled_quantity, filled_value,
                                filled_commission, version, created_at, updated_at)
            VALUES ({order.Id.Value}, {order.Client.Value}, {order.ClientKey}, {order.Symbol}, {order.Side.ToString()},
                    {order.Type.ToString()}, {order.LimitPrice}, {order.Quantity}, {order.BlockedAmount},
                    {order.Status.ToString()}, {order.FundsBlocked}, {order.FundsReleased}, {order.SubmitAttempts},
                    {order.Execution.Quantity}, {order.Execution.Value}, {order.Execution.Commission}, {order.Version}, {now}, {now})
            ON CONFLICT (client_id, client_key) DO NOTHING
            """, cancellationToken);
        var row = await db.Orders.AsNoTracking()
            .SingleAsync(o => o.ClientId == order.Client.Value && o.ClientKey == order.ClientKey, cancellationToken);
        return ToOrder(row);
    }

    public async Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id.Value, cancellationToken);
        return row is null ? null : ToOrder(row);
    }

    public async Task<IReadOnlyList<Order>> ClientOrdersAsync(ClientId client, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.ClientId == client.Value)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(ToOrder).ToList();
    }

    public async Task SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var updated = await db.Orders
            .Where(o => o.Id == order.Id.Value && o.Version == order.Version)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, order.Status)
                .SetProperty(o => o.ExchangeOrderId, order.ExchangeOrderId)
                .SetProperty(o => o.RejectionReason, order.RejectionReason)
                .SetProperty(o => o.FundsBlocked, order.FundsBlocked)
                .SetProperty(o => o.FundsReleased, order.FundsReleased)
                .SetProperty(o => o.SubmitAttempts, order.SubmitAttempts)
                .SetProperty(o => o.FilledQuantity, order.Execution.Quantity)
                .SetProperty(o => o.FilledValue, order.Execution.Value)
                .SetProperty(o => o.FilledCommission, order.Execution.Commission)
                .SetProperty(o => o.LastEventRunId, order.Execution.LastEvent == null ? null : order.Execution.LastEvent.RunId)
                .SetProperty(o => o.LastEventSequence, order.Execution.LastEvent == null ? null : order.Execution.LastEvent.Sequence)
                .SetProperty(o => o.Version, order.Version + 1)
                .SetProperty(o => o.UpdatedAt, clock.GetUtcNow()), cancellationToken);
        if (updated == 0)
        {
            throw new OrderConcurrencyException(order.Id);
        }
    }

    public async Task<IReadOnlyList<Order>> NeedingAttentionAsync(DateTimeOffset updatedBefore, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // Ten sam warunek co Order.NeedsAttention i filtr indeksu ix_orders_needing_attention.
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Unknown
                        || ((o.Status == OrderStatus.Rejected || o.Status == OrderStatus.Filled || o.Status == OrderStatus.Cancelled)
                            && o.FundsBlocked && !o.FundsReleased))
            .Where(o => o.UpdatedAt < updatedBefore)
            // Po ostatniej zmianie: kazda proba zapisuje sie przed wyslaniem, wiec zlecenie, ktore ciagle
            // sie nie udaje, przesuwa sie na koniec i nie blokuje pozostalych.
            .OrderBy(o => o.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(ToOrder).ToList();
    }

    public async Task<IReadOnlyList<Order>> ByExchangeOrderIdAsync(string symbol, long exchangeOrderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.Symbol == symbol && o.ExchangeOrderId == exchangeOrderId)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(ToOrder).ToList();
    }

    public async Task<bool> HasInFlightAsync(string symbol, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // Ten sam warunek co Order.IsInFlight.
        return await db.Orders.AnyAsync(o => o.Symbol == symbol
            && (o.Status == OrderStatus.Unknown || (o.Status == OrderStatus.Pending && o.SubmitAttempts > 0)), cancellationToken);
    }

    private static Order ToOrder(OrderRow row) => Order.Restore(
        new OrderId(row.Id), new ClientId(row.ClientId), row.ClientKey,
        new OrderRequest(row.Symbol, row.Side, row.Type, row.LimitPrice, row.Quantity),
        row.BlockedAmount, row.Status, row.ExchangeOrderId, row.RejectionReason,
        row.FundsBlocked, row.FundsReleased, row.SubmitAttempts, row.Version,
        new OrderExecution(row.FilledQuantity, row.FilledValue, row.FilledCommission,
            row.LastEventRunId is { } run && row.LastEventSequence is { } sequence ? new EventPosition(run, sequence) : null));
}
