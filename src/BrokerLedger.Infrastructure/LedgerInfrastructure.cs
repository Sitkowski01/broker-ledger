using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace BrokerLedger.Infrastructure;

/// <summary>Jedyne publiczne wejscie do tej warstwy: rejestracja w DI, health check i migracje.</summary>
public static class LedgerInfrastructure
{
    public static IServiceCollection AddLedgerInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddPooledDbContextFactory<LedgerDbContext>(options => LedgerDb.Configure(options, connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILedgerStore, PostgresLedgerStore>();
        services.AddSingleton<ILedgerQueries, PostgresLedgerQueries>();
        services.AddSingleton<IClientRepository, PostgresClientRepository>();
        services.AddSingleton<IOrderRepository, PostgresOrderRepository>();
        services.AddSingleton<IExchangeInbox, PostgresExchangeInbox>();
        services.AddSingleton<PostgresDeadLetters>();
        services.AddSingleton<IMarketPrices, PostgresMarketPrices>();
        // Jedna instancja: nasluch w tle (hosted service) i subskrypcje (IClientEvents) dziela liste subskrybentow.
        services.AddSingleton(new ClientEventsSettings(connectionString, TimeSpan.FromSeconds(2)));
        services.AddSingleton<PostgresClientEvents>();
        services.AddSingleton<IClientEvents>(provider => provider.GetRequiredService<PostgresClientEvents>());
        services.AddHostedService(provider => provider.GetRequiredService<PostgresClientEvents>());
        return services;
    }

    /// <summary>Nazwa klienta HTTP do NBP — testy podmieniaja pod nia serwer.</summary>
    public const string NbpHttpClient = "Nbp";

    /// <summary>
    /// Kursy srednie NBP. Inaczej niz przy gieldzie ponowienia sa tu bezpieczne: GET niczego nie zmienia.
    /// Kazda proba ma wlasny limit czasu, a bezpiecznik po serii bledow przestaje meczyc lezace API.
    /// </summary>
    public static IServiceCollection AddNbpExchangeRates(this IServiceCollection services, Uri baseUrl,
        TimeSpan attemptTimeout, TimeSpan cacheFor, TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(attemptTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cacheFor, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retryAfter, TimeSpan.Zero);
        services.AddHttpClient(NbpHttpClient, http => http.BaseAddress = baseUrl)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.FromMilliseconds(200);
                options.AttemptTimeout.Timeout = attemptTimeout;
                // Trzy proby z przerwami musza sie zmiescic w limicie calosci.
                options.TotalRequestTimeout.Timeout = attemptTimeout * 3 + TimeSpan.FromSeconds(1);
                // Biblioteka wymaga okna bezpiecznika co najmniej dwa razy dluzszego niz jedna proba.
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(30).Ticks, attemptTimeout.Ticks * 2));
                options.CircuitBreaker.MinimumThroughput = 5;
            });
        services.AddSingleton<IExchangeRates>(provider => new CachedExchangeRates(
            ct => new NbpTableSource(provider.GetRequiredService<IHttpClientFactory>().CreateClient(NbpHttpClient)).LatestAsync(ct),
            provider.GetRequiredService<TimeProvider>(), cacheFor, retryAfter,
            provider.GetRequiredService<ILogger<CachedExchangeRates>>()));
        return services;
    }

    /// <summary>
    /// Klient gieldy Parkiet. Bez automatycznych ponowien w HttpClient: ponawianie zlecen nalezy do sagi
    /// (z tym samym clientOrderId), nie do warstwy HTTP, ktora nie wie, czy zlecenie juz weszlo.
    /// </summary>
    public static IServiceCollection AddParkietExchange(this IServiceCollection services, Uri baseUrl, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        services.AddHttpClient<IExchange, ParkietExchange>(http =>
        {
            http.BaseAddress = baseUrl;
            http.Timeout = timeout;
        });
        return services;
    }

    /// <summary>
    /// Konsument zdarzen gieldy z Kafki (w tle) — odklada je do skrzynki. Grupa konsumentow pozwala uruchomic
    /// kilka instancji brokera: kazda partycja (spolka) trafia do jednej z nich.
    /// </summary>
    public static IServiceCollection AddExchangeEventsConsumer(this IServiceCollection services,
        string bootstrapServers, string topic, string groupId, TimeSpan retryDelay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retryDelay, TimeSpan.Zero);
        services.AddSingleton(new ExchangeEventsSettings(bootstrapServers, topic, groupId, retryDelay));
        services.AddHostedService<ExchangeEventConsumer>();
        return services;
    }

    /// <summary>Aplikacja jest zdrowa tylko wtedy, gdy dosiega bazy — bez niej nie zaksieguje niczego.</summary>
    public static IHealthChecksBuilder AddLedgerDatabaseCheck(this IHealthChecksBuilder builder) =>
        builder.AddCheck<LedgerDatabaseHealthCheck>("database");

    public static async Task MigrateLedgerDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var contexts = services.GetRequiredService<IDbContextFactory<LedgerDbContext>>();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}

internal sealed class LedgerDatabaseHealthCheck(IDbContextFactory<LedgerDbContext> contexts) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("cannot reach the ledger database");
    }
}
