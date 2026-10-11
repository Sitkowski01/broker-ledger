using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

/// <summary>Jedna konfiguracja Npgsql dla aplikacji, migracji (dotnet ef) i testow.</summary>
internal static class LedgerDb
{
    public static TBuilder Configure<TBuilder>(TBuilder builder, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        return builder;
    }
}
