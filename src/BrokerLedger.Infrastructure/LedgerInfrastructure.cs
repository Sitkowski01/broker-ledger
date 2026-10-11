using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
