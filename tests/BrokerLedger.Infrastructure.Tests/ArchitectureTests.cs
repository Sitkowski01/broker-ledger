using System.Reflection;

namespace BrokerLedger.Infrastructure.Tests;

public class ArchitectureTests
{
    private static readonly Assembly Infrastructure = typeof(PostgresLedgerStore).Assembly;

    [Fact]
    public void Infrastructure_does_not_know_about_the_web_layer()
    {
        var references = Infrastructure.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(references, name => name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain("BrokerLedger.Api", references);
    }

    [Fact]
    public void Persistence_details_stay_internal()
    {
        // Reszta aplikacji zna tylko ILedgerStore z domeny — wiersze i DbContext sa szczegolem tej warstwy.
        // Wyjatek: migracje, ktore dotnet ef generuje jako publiczne.
        var publicTypes = Infrastructure.GetExportedTypes().Where(t =>
            t.Namespace!.StartsWith("BrokerLedger.Infrastructure", StringComparison.Ordinal)
            && t.Namespace != "BrokerLedger.Infrastructure.Migrations");

        Assert.Empty(publicTypes);
    }
}
