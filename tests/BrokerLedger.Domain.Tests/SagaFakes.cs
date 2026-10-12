using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

/// <summary>Ksiega w pamieci z kluczami idempotencji — jak PostgresLedgerStore, tylko bez bazy.</summary>
internal sealed class InMemoryLedgerStore : ILedgerStore
{
    private readonly Dictionary<string, JournalEntry> _byKey = [];

    public Ledger Ledger { get; } = new();

    /// <summary>Klucz, przy ktorym kolejne ksiegowanie "padnie" (symulacja awarii bazy).</summary>
    public string? FailOnce { get; set; }

    public Task<PostOutcome> PostAsync(string idempotencyKey, JournalEntry entry, CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == FailOnce)
        {
            FailOnce = null;
            throw new TimeoutException("database did not answer");
        }
        if (_byKey.TryGetValue(idempotencyKey, out var existing))
        {
            return Task.FromResult(existing.HasSameContentAs(entry)
                ? PostOutcome.AlreadyPosted
                : throw new IdempotencyConflictException(idempotencyKey));
        }
        Ledger.Post(entry);
        _byKey[idempotencyKey] = entry;
        return Task.FromResult(PostOutcome.Posted);
    }

    public Task<decimal> BalanceAsync(AccountId account, CancellationToken cancellationToken = default) =>
        Task.FromResult(Ledger.Balance(account));
}

/// <summary>Repozytorium w pamieci z wersjami jak w bazie; potrafi "pasc" przy wybranym zapisie.</summary>
internal sealed class InMemoryOrders : IOrderRepository
{
    private readonly Dictionary<OrderId, Order> _stored = [];

    public int Saves { get; private set; }

    /// <summary>Numer zapisu (od 1), przy ktorym repozytorium rzuci wyjatkiem zamiast zapisac.</summary>
    public int? FailOnSave { get; set; }

    public Task<Order> AddOrGetAsync(Order order, CancellationToken cancellationToken = default)
    {
        var existing = _stored.Values.FirstOrDefault(o => o.Client == order.Client && o.ClientKey == order.ClientKey);
        if (existing is not null)
        {
            return Task.FromResult(Copy(existing));
        }
        _stored[order.Id] = Copy(order);
        return Task.FromResult(Copy(order));
    }

    public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_stored.TryGetValue(id, out var order) ? Copy(order) : null);

    public Task<IReadOnlyList<Order>> ClientOrdersAsync(ClientId client, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Order>>(_stored.Values.Where(o => o.Client == client).Take(limit).Select(Copy).ToList());

    public Task SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        Saves++;
        if (Saves == FailOnSave)
        {
            throw new TimeoutException("database did not answer");
        }
        if (_stored[order.Id].Version != order.Version)
        {
            throw new OrderConcurrencyException(order.Id);
        }
        var saved = Copy(order);
        saved.MarkSaved();
        _stored[order.Id] = saved;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Order>> NeedingAttentionAsync(DateTimeOffset updatedBefore, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Order>>(_stored.Values.Where(o => o.NeedsAttention).Take(limit).Select(Copy).ToList());

    /// <summary>Kopia jak po odczycie z bazy — test nie moze przypadkiem dzielic obiektu z repozytorium.</summary>
    private static Order Copy(Order o) => Order.Restore(o.Id, o.Client, o.ClientKey, o.Request, o.BlockedAmount,
        o.Status, o.ExchangeOrderId, o.RejectionReason, o.FundsBlocked, o.FundsReleased, o.SubmitAttempts, o.Version,
        o.Execution);
}

/// <summary>Gielda, ktora odpowiada po kolei tym, co test jej zada.</summary>
internal sealed class ScriptedExchange : IExchange
{
    private readonly Queue<Func<ExchangeReply>> _replies = new();
    private readonly Queue<CancelReply> _cancels = new();

    public List<string> Placed { get; } = [];
    public List<long?> Cancelled { get; } = [];

    public ScriptedExchange Then(ExchangeReply reply)
    {
        _replies.Enqueue(() => reply);
        return this;
    }

    public ScriptedExchange ThenThrow(Exception failure)
    {
        _replies.Enqueue(() => throw failure);
        return this;
    }

    public ScriptedExchange ThenCancel(CancelReply reply)
    {
        _cancels.Enqueue(reply);
        return this;
    }

    public Task<ExchangeReply> PlaceAsync(Order order, CancellationToken cancellationToken = default)
    {
        Placed.Add(order.Id.ForExchange);
        return Task.FromResult(_replies.Dequeue()());
    }

    public Task<CancelReply> CancelAsync(Order order, CancellationToken cancellationToken = default)
    {
        Cancelled.Add(order.ExchangeOrderId);
        return Task.FromResult(_cancels.Dequeue());
    }
}
