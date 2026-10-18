using BrokerLedger.Domain;
using Microsoft.Extensions.Options;

namespace BrokerLedger.Api.Orders;

/// <summary>
/// Dokancza sagi, ktore sie nie skonczyly: zlecenia bez odpowiedzi gieldy, po awarii w polowie albo
/// z niezwolniona blokada. Kazdy krok sagi jest idempotentny, wiec powtorzenie niczego nie zdubluje.
/// </summary>
internal sealed partial class OrderReconciler(
    IServiceScopeFactory scopes, IOptions<ReconcilerOptions> options, TimeProvider clock, ILogger<OrderReconciler> logger)
    : BackgroundService
{
    /// <summary>Co ile prob o zleceniu, ktore wciaz jest "nieznane", ostrzegamy w logu.</summary>
    internal const int StuckWarningEvery = 10;

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

    /// <summary>
    /// Przebieg, ktory nigdy nie rzuca — poza zamykaniem aplikacji. Chwilowy brak bazy nie moze zatrzymac
    /// reconcilera, a wyjatek z BackgroundService domyslnie zatrzymuje cala aplikacje (StopHost).
    /// </summary>
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

    /// <summary>Jeden przebieg: ile zlecen doprowadzono do konca albo dalej.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var saga = scope.ServiceProvider.GetRequiredService<OrderSaga>();

        var progressed = 0;
        var batch = await orders.NeedingAttentionAsync(clock.GetUtcNow() - settings.MinAge, settings.BatchSize, cancellationToken);
        foreach (var order in batch)
        {
            try
            {
                var result = await saga.ProgressAsync(order, cancellationToken);
                progressed++;
                // Nieznane zlecenie wstrzymuje rozliczanie calej spolki (transakcja moze byc jego), wiec nie moze
                // wisiec po cichu — np. gdy gielda w kolko odpowiada 409 albo bez numeru zlecenia.
                if (result.Status == OrderStatus.Unknown && result.SubmitAttempts % StuckWarningEvery == 0)
                {
                    LogStuck(logger, result.Id.Value, result.Request.Symbol, result.SubmitAttempts);
                }
            }
            catch (OrderConcurrencyException)
            {
                // Ktos inny (zadanie klienta) wlasnie je zmienil — wroci w nastepnym przebiegu, jesli trzeba.
            }
            // Stryker disable Block : w bloku jest tylko logowanie
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Jedno zlecenie (np. baza na chwile niedostepna) nie zatrzymuje pozostalych.
                // Stryker disable once Statement : samo logowanie, bez wplywu na dzialanie
                LogFailure(logger, order.Id.Value, e);
            }
            // Stryker restore Block
        }
        return progressed;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Order {OrderId} on {Symbol} is still unknown after {Attempts} attempts; settlement of this company waits for it")]
    private static partial void LogStuck(ILogger logger, Guid orderId, string symbol, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation pass failed")]
    private static partial void LogPassFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciling order {OrderId} failed")]
    private static partial void LogFailure(ILogger logger, Guid orderId, Exception exception);
}
