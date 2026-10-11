using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BrokerLedger.Infrastructure;

/// <summary>Tylko dla <c>dotnet ef migrations</c> — narzedzie potrzebuje kontekstu bez uruchamiania aplikacji.</summary>
internal sealed class LedgerDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args) =>
        new(LedgerDb.Configure(new DbContextOptionsBuilder<LedgerDbContext>(), "Host=localhost;Database=broker_ledger").Options);
}
