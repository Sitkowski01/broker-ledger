using BrokerLedger.Api.Orders;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BrokerLedger.Api.Tests;

public partial class ReconcilerTests
{
    private sealed class Orders(params Order[] orders) : IOrderRepository
    {
        public Order[] All { get; } = orders;
        public Queue<Exception> SaveFailures { get; } = new();
        public (DateTimeOffset, int)? LastQuery { get; private set; }
        public int FailQueries { get; set; }
        public Exception? FailWith { get; set; }
        public int Queries { get; private set; }

        public Task<IReadOnlyList<Order>> NeedingAttentionAsync(DateTimeOffset updatedBefore, int limit, CancellationToken cancellationToken = default)
        {
            if (++Queries <= FailQueries)
            {
                throw new TimeoutException("database");
            }
            if (FailWith is not null)
            {
                throw FailWith;
            }
            LastQuery = (updatedBefore, limit);
            return Task.FromResult<IReadOnlyList<Order>>(All);
        }

        public Task SaveAsync(Order order, CancellationToken cancellationToken = default) =>
            SaveFailures.TryDequeue(out var failure) ? Task.FromException(failure) : Task.CompletedTask;

        public Task<Order> AddOrGetAsync(Order order, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Order>> ClientOrdersAsync(ClientId client, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Order>> ByExchangeOrderIdAsync(string symbol, long exchangeOrderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasInFlightAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Exchange : IExchange
    {
        public int Calls { get; private set; }
        public bool ThrowCancelled { get; init; }

        public Task<ExchangeReply> PlaceAsync(Order order, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ExchangeReply>(new ExchangeReply.Accepted(Calls));
        }

        public Task<CancelReply> CancelAsync(Order order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoLedger : ILedgerStore
    {
        public Task<PostOutcome> PostAsync(string idempotencyKey, JournalEntry entry, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<decimal> BalanceAsync(AccountId account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
