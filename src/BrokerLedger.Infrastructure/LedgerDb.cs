using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

internal static class LedgerDb
{
    public static DbContextOptionsBuilder<LedgerDbContext> Configure(
        DbContextOptionsBuilder<LedgerDbContext> builder, string connectionString) =>
        builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}
