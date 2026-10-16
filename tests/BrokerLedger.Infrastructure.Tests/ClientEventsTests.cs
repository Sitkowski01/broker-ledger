using BrokerLedger.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Sygnaly z wyzwalaczy PostgreSQL (NOTIFY) przez nasluch do subskrybentow wlasciwego klienta.</summary>
[Collection(SharedPostgres.Name)]
public sealed class ClientEventsTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly ClientId _client = ClientId.New();
    private readonly CancellationTokenSource _stop = new();
    private TestDatabase _db = null!;
    private PostgresClientEvents _events = null!;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        _events = new PostgresClientEvents(new ClientEventsSettings(_db.ConnectionString, TimeSpan.FromMilliseconds(100)),
            NullLogger<PostgresClientEvents>.Instance);
        await _events.StartAsync(_stop.Token);
        await Until(() => _events.Connections == 1);
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        await _events.StopAsync(CancellationToken.None);
        await _db.DisposeAsync();
    }

    public void Dispose()
    {
        _events.Dispose();
        _stop.Dispose();
    }

    [Fact]
    public async Task A_document_on_the_clients_account_is_one_ledger_signal()
    {
        await using var events = _events.SubscribeAsync(_client, _stop.Token).GetAsyncEnumerator();
        var first = events.MoveNextAsync().AsTask();

        // Wplata ma dwa zapisy (bank i klient), ale klient dostaje jeden sygnal.
        await _db.Store().PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(100m)));

        Assert.True(await first.WaitAsync(Wait));
        Assert.Equal(new ClientEvent(_client, ClientEventKind.Ledger), events.Current);
        await _db.Store().PostAsync("wd", JournalEntries.Withdrawal(_client, Money.Pln(1m)));
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(Wait));
        Assert.Equal(ClientEventKind.Ledger, events.Current.Kind);
    }

    [Fact]
    public async Task Every_order_change_says_which_order_and_its_status()
    {
        await using var events = _events.SubscribeAsync(_client, _stop.Token).GetAsyncEnumerator();
        var order = Order.Place(OrderId.New(), _client, "k", new OrderRequest("CDR", OrderSide.Sell, OrderType.Market, null, 5), Commission.Standard);
        var orders = _db.Orders();
        // Subskrypcja zaczyna sie przy pierwszym MoveNextAsync — przed zmiana.
        var first = events.MoveNextAsync().AsTask();

        await orders.AddOrGetAsync(order);
        Assert.True(await first.WaitAsync(Wait));
        Assert.Equal(new ClientEvent(_client, ClientEventKind.Order, order.Id, OrderStatus.Pending), events.Current);

        order.MarkFundsBlocked();
        order.Reject("queue full");
        await orders.SaveAsync(order);
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(Wait));
        Assert.Equal(OrderStatus.Rejected, events.Current.Status);
    }

    [Fact]
    public async Task Another_clients_documents_never_reach_the_subscriber()
    {
        await using var events = _events.SubscribeAsync(_client, _stop.Token).GetAsyncEnumerator();
        var next = events.MoveNextAsync().AsTask();

        await _db.Store().PostAsync("other", JournalEntries.Deposit(ClientId.New(), Money.Pln(5m)));
        await _db.Store().PostAsync("mine", JournalEntries.Deposit(_client, Money.Pln(7m)));

        // Pierwszy sygnal, jaki przychodzi, to juz wlasna wplata — cudza nie dotarla.
        Assert.True(await next.WaitAsync(Wait));
        Assert.Equal(_client, events.Current.Client);
    }

    [Fact]
    public async Task After_a_lost_connection_every_subscriber_is_told_to_read_everything_again()
    {
        await using var events = _events.SubscribeAsync(_client, _stop.Token).GetAsyncEnumerator();
        var next = events.MoveNextAsync().AsTask();

        await _db.ExecuteAsync("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE query LIKE 'LISTEN%' AND pid <> pg_backend_pid()");

        Assert.True(await next.WaitAsync(Wait));
        Assert.Equal(new ClientEvent(_client, ClientEventKind.Resync), events.Current);
        Assert.Equal(2, _events.Connections);
        // Po powrocie nasluch dziala dalej.
        await _db.Store().PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(1m)));
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(Wait));
        Assert.Equal(ClientEventKind.Ledger, events.Current.Kind);
    }

    [Fact]
    public async Task A_closed_subscription_stops_receiving()
    {
        using var mine = new CancellationTokenSource();
        var events = _events.SubscribeAsync(_client, mine.Token).GetAsyncEnumerator(mine.Token);
        var next = events.MoveNextAsync().AsTask();
        await mine.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
        await events.DisposeAsync();

        // Sygnal do klienta bez subskrybentow nie ma dokad trafic — i niczego nie psuje.
        _events.Dispatch($$"""{"client":"{{_client}}","kind":"ledger"}""");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"kind":"ledger"}""")]
    [InlineData("""{"client":"00000000-0000-0000-0000-000000000000","kind":"ledger"}""")]
    [InlineData("""{"client":"0190a000-0000-7000-8000-000000000001","kind":"gift"}""")]
    [InlineData("""{"client":"0190a000-0000-7000-8000-000000000001","kind":"order","order":"x","status":"Accepted"}""")]
    [InlineData("""{"client":"0190a000-0000-7000-8000-000000000001","kind":"order","order":"0190a000-0000-7000-8000-000000000002","status":"Lost"}""")]
    [InlineData("""{"client":"0190a000-0000-7000-8000-000000000001","kind":"order"}""")]
    public void Malformed_signals_are_ignored(string payload) => Assert.Null(PostgresClientEvents.Parse(payload));

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met in time");
            await Task.Delay(20);
        }
    }
}
