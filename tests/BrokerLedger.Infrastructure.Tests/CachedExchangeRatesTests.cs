using BrokerLedger.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerLedger.Infrastructure.Tests;

public sealed class CachedExchangeRatesTests
{
    private static readonly ExchangeRateTable First = new("1/A/NBP/2026", new DateOnly(2026, 1, 2), new Dictionary<Currency, decimal>());
    private static readonly ExchangeRateTable Second = new("2/A/NBP/2026", new DateOnly(2026, 1, 5), new Dictionary<Currency, decimal>());

    private readonly ManualClock _clock = new();
    private readonly Queue<Func<ExchangeRateTable>> _answers = new();
    private int _fetches;

    private CachedExchangeRates Rates() => new(_ =>
        {
            _fetches++;
            return Task.FromResult(_answers.Dequeue()());
        }, _clock, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), NullLogger<CachedExchangeRates>.Instance);

    private static ExchangeRateTable Down() => throw new HttpRequestException("NBP is down");

    [Fact]
    public async Task Table_is_kept_until_it_expires()
    {
        _answers.Enqueue(() => First);
        _answers.Enqueue(() => Second);
        using var rates = Rates();

        Assert.Equal(new RatesSnapshot(First, false), await rates.CurrentAsync());
        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(new RatesSnapshot(First, false), await rates.CurrentAsync());
        Assert.Equal(1, _fetches);

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new RatesSnapshot(Second, false), await rates.CurrentAsync());
        Assert.Equal(2, _fetches);
    }

    [Fact]
    public async Task Without_any_table_a_failure_gives_nothing_and_waits_before_asking_again()
    {
        _answers.Enqueue(Down);
        _answers.Enqueue(() => First);
        using var rates = Rates();

        Assert.Null(await rates.CurrentAsync());
        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Null(await rates.CurrentAsync());
        Assert.Equal(1, _fetches);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new RatesSnapshot(First, false), await rates.CurrentAsync());
    }

    [Fact]
    public async Task Failed_refresh_serves_the_last_table_marked_stale()
    {
        _answers.Enqueue(() => First);
        _answers.Enqueue(Down);
        _answers.Enqueue(() => Second);
        using var rates = Rates();
        await rates.CurrentAsync();

        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(new RatesSnapshot(First, true), await rates.CurrentAsync());
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(new RatesSnapshot(First, true), await rates.CurrentAsync());
        Assert.Equal(2, _fetches);

        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(new RatesSnapshot(Second, false), await rates.CurrentAsync());
    }

    [Fact]
    public async Task Concurrent_callers_share_one_request()
    {
        var release = new TaskCompletionSource<ExchangeRateTable>();
        var calls = 0;
        using var rates = new CachedExchangeRates(_ =>
            {
                Interlocked.Increment(ref calls);
                return release.Task;
            }, _clock, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), NullLogger<CachedExchangeRates>.Instance);

        var waiting = Enumerable.Range(0, 10).Select(_ => rates.CurrentAsync()).ToList();
        release.SetResult(First);

        Assert.All(await Task.WhenAll(waiting), snapshot => Assert.Equal(new RatesSnapshot(First, false), snapshot));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancelled_caller_gets_a_cancellation_not_a_stale_table()
    {
        using var cancel = new CancellationTokenSource();
        using var rates = new CachedExchangeRates(async ct =>
            {
                await cancel.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return First;
            }, _clock, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), NullLogger<CachedExchangeRates>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rates.CurrentAsync(cancel.Token));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
