using BrokerLedger.Domain;
using Microsoft.Extensions.Options;

namespace BrokerLedger.Api.Orders;

/// <summary>Sekcja <c>Orders:Settlement</c>: jak czesto rozliczac zdarzenia ze skrzynki.</summary>
internal sealed class SettlementOptions
{
    public const string Section = "Orders:Settlement";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Ile zdarzen naraz z kazdej spolki.</summary>
    public int BatchSize { get; set; } = 200;

    public bool IsValid() => Interval > TimeSpan.Zero && BatchSize > 0;
}

/// <summary>
/// Rozlicza w tle zdarzenia, ktore konsument Kafki odlozyl do skrzynki. Jak reconciler: przebieg nigdy nie
/// zatrzymuje aplikacji, a spolka z bledem czeka do nastepnego przebiegu, nie blokujac pozostalych.
/// </summary>
internal sealed partial class SettlementWorker(
    IServiceScopeFactory scopes, IOptions<SettlementOptions> options, TimeProvider clock, ILogger<SettlementWorker> logger)
    : BackgroundService
{
    /// <summary>
    /// Ostatni zgloszony blad kazdej spolki. Blad wymagajacy czlowieka (np. transakcja niepasujaca do zlecenia) powtarza sie
    /// w kazdym przebiegu — logujemy go raz, a nie kilka razy na sekunde.
    /// </summary>
    private readonly Dictionary<string, string> _reported = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }
        using var timer = new PeriodicTimer(settings.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TickAsync(stoppingToken);
        }
    }

    public async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunOnceAsync(stoppingToken);
        }
        // Stryker disable Block : w bloku jest tylko logowanie
        catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Stryker disable once Statement : samo logowanie, bez wplywu na dzialanie
            LogPassFailure(logger, e);
        }
        // Stryker restore Block
    }

    /// <summary>Jeden przebieg: ile zdarzen rozliczono.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var settlement = scope.ServiceProvider.GetRequiredService<TradeSettlement>();
        var pass = await settlement.SettlePendingAsync(options.Value.BatchSize, cancellationToken);
        scope.ServiceProvider.GetRequiredService<BrokerMetrics>().Settled(pass.Settled);
        foreach (var (exchangeEvent, error) in pass.Failures)
        {
            var failure = $"{exchangeEvent.Position} {error.GetType().Name}: {error.Message}";
            if (_reported.GetValueOrDefault(exchangeEvent.Symbol) != failure)
            {
                _reported[exchangeEvent.Symbol] = failure;
                LogFailure(logger, exchangeEvent.Symbol, exchangeEvent.Position.ToString(), error);
            }
        }
        return pass.Settled;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Settlement pass failed")]
    private static partial void LogPassFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settling {Symbol} event {Position} failed; the symbol waits for the next pass")]
    private static partial void LogFailure(ILogger logger, string symbol, string position, Exception exception);
}
