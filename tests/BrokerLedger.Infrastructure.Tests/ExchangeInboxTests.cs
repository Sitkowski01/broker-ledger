using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

[Collection(SharedPostgres.Name)]
public sealed class ExchangeInboxTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;
    private PostgresExchangeInbox _inbox = null!;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        _inbox = _db.Inbox();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static ExchangeEvent.Trade Trade(long sequence, string symbol = "CDR", long run = 1) =>
        new(symbol, new EventPosition(run, sequence), 4, 9, OrderSide.Sell, 250.40m, 3);

    private static ExchangeEvent.Cancelled Cancel(long sequence, string symbol = "CDR") =>
        new(symbol, new EventPosition(1, sequence), 4, 6, "NO_LIQUIDITY");

    [Fact]
    public async Task Reads_back_both_kinds_of_events_in_arrival_order()
    {
        Assert.True(await _inbox.AddAsync(Trade(2)));
        Assert.True(await _inbox.AddAsync(Cancel(1, "PKN")));

        var pending = await _inbox.PendingAsync(10);

        Assert.Equal(new ExchangeEvent[] { Trade(2), Cancel(1, "PKN") }, pending);
    }

    [Fact]
    public async Task Repeated_event_is_stored_once()
    {
        await _inbox.AddAsync(Trade(1));

        Assert.False(await _inbox.AddAsync(Trade(1)));
        Assert.True(await _inbox.AddAsync(Trade(1, "PKN")));
        Assert.True(await _inbox.AddAsync(Trade(1, run: 2)));
        Assert.Equal(3, await _db.CountAsync("exchange_inbox"));
    }

    [Fact]
    public async Task Parallel_consumers_store_one_copy()
    {
        var added = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _inbox.AddAsync(Trade(1))));

        Assert.Single(added, true);
    }

    [Fact]
    public async Task Settled_events_leave_the_queue()
    {
        await _inbox.AddAsync(Trade(1));
        await _inbox.AddAsync(Trade(2));
        await _inbox.AddAsync(Trade(1, "PKN"));

        await _inbox.MarkSettledAsync(Trade(1));

        Assert.Equal([Trade(2), Trade(1, "PKN")], await _inbox.PendingAsync(10));
        // Limit dotyczy kazdej spolki osobno — stojaca spolka nie zabierze miejsca innym.
        Assert.Equal([Trade(2), Trade(1, "PKN")], await _inbox.PendingAsync(1));
        await _inbox.AddAsync(Trade(3));
        Assert.Equal([Trade(2), Trade(1, "PKN")], await _inbox.PendingAsync(1));
        Assert.Equal([Trade(2), Trade(1, "PKN"), Trade(3)], await _inbox.PendingAsync(2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _inbox.PendingAsync(0));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _inbox.AddAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _inbox.MarkSettledAsync(null!));
    }

    [Fact]
    public async Task Settling_keeps_the_first_settlement_time()
    {
        await _inbox.AddAsync(Trade(1));
        await _inbox.MarkSettledAsync(Trade(1));
        await _db.ExecuteAsync("UPDATE exchange_inbox SET settled_at = '2020-01-01Z'");

        await _inbox.MarkSettledAsync(Trade(1));

        Assert.Equal(1, await _db.ExecuteAsync("UPDATE exchange_inbox SET quantity = quantity WHERE settled_at = '2020-01-01Z'"));
    }

    [Theory]
    [InlineData("type = 'RESTED'")]
    [InlineData("price = NULL")]
    [InlineData("order_id = 1")]
    [InlineData("quantity = 0")]
    public async Task Database_refuses_malformed_trades(string change)
    {
        await _inbox.AddAsync(Trade(1));

        await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync($"UPDATE exchange_inbox SET {change}"));
    }

    [Theory]
    [InlineData("reason = NULL")]
    [InlineData("order_id = NULL")]
    [InlineData("maker_order_id = 1")]
    public async Task Database_refuses_malformed_cancellations(string change)
    {
        await _inbox.AddAsync(Cancel(1));

        await Assert.ThrowsAnyAsync<Exception>(() => _db.ExecuteAsync($"UPDATE exchange_inbox SET {change}"));
    }
}
