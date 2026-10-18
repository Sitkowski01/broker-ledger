using BrokerLedger.Api.Events;
using BrokerLedger.Domain;

namespace BrokerLedger.Api.Tests;

/// <summary>Strumien SSE bez serwera: kolejnosc "ready", zdarzen i "ping" oraz zachowanie przy rozlaczeniu.</summary>
public class EventItemsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly ClientId Client = ClientId.New();

    [Fact]
    public async Task Quiet_stream_keeps_pinging()
    {
        var source = new ManualSource { Endless = true };
        var items = EventEndpoints.ItemsAsync(source, TimeSpan.FromMilliseconds(10), CancellationToken.None).GetAsyncEnumerator();

        var names = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await items.MoveNextAsync().AsTask().WaitAsync(Wait));
            names.Add(items.Current.EventType!);
        }

        Assert.Equal(["ready", "ping", "ping"], names);
        source.Finish();
        await items.DisposeAsync();
    }

    [Fact]
    public async Task Stream_ends_when_the_source_ends()
    {
        var source = new ManualSource(new ClientEvent(Client, ClientEventKind.Ledger), new ClientEvent(Client, ClientEventKind.Resync));
        await using var items = EventEndpoints.ItemsAsync(source, Wait, CancellationToken.None).GetAsyncEnumerator();

        // Z limitem: strumien, ktory sie nie konczy, ma oblac test, a nie go zawiesic.
        var names = new List<string>();
        while (names.Count < 10 && await items.MoveNextAsync().AsTask().WaitAsync(Wait))
        {
            names.Add(items.Current.EventType!);
        }

        Assert.Equal(["ready", "ledger", "resync"], names);
    }

    [Fact]
    public async Task Disconnect_while_waiting_ends_the_stream_and_lets_the_source_finish_first()
    {
        using var disconnect = new CancellationTokenSource();
        var source = new ManualSource { Endless = true };
        var items = EventEndpoints.ItemsAsync(source, Wait, disconnect.Token).GetAsyncEnumerator();
        Assert.True(await items.MoveNextAsync());

        await disconnect.CancelAsync();
        var next = items.MoveNextAsync().AsTask();
        source.Finish();

        // Rozlaczenie to koniec, nie kolejny "ping" — a zrodla nie wolno zamknac, dopoki czeka na zdarzenie.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(Wait));
        Assert.False(source.DisposedWhileWaiting);
        await items.DisposeAsync();
    }

    /// <summary>Zrodlo zdarzen sterowane z testu; z <see cref="Endless"/> po zdarzeniach czeka na <see cref="Finish"/>.</summary>
    private sealed class ManualSource(params ClientEvent[] events) : IAsyncEnumerable<ClientEvent>, IAsyncEnumerator<ClientEvent>
    {
        private readonly Queue<ClientEvent> _events = new(events);
        private TaskCompletionSource<bool>? _waiting;

        public bool Endless { get; init; }

        public bool DisposedWhileWaiting { get; private set; }

        public ClientEvent Current { get; private set; } = null!;

        public IAsyncEnumerator<ClientEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            if (_events.TryDequeue(out var next))
            {
                Current = next;
                return ValueTask.FromResult(true);
            }
            if (!Endless)
            {
                return ValueTask.FromResult(false);
            }
            _waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return new ValueTask<bool>(_waiting.Task);
        }

        public void Finish() => _waiting?.TrySetResult(false);

        public ValueTask DisposeAsync()
        {
            DisposedWhileWaiting |= _waiting is { Task.IsCompleted: false };
            return ValueTask.CompletedTask;
        }
    }
}
