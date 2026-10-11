using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Publiczne wejscie warstwy: to, czego uzywa aplikacja przy starcie.</summary>
[Collection(SharedPostgres.Name)]
public sealed class LedgerInfrastructureTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Registers_working_ports_and_migrates_an_empty_database()
    {
        await using var db = await postgres.CreateEmptyDatabaseAsync();
        await using var services = new ServiceCollection().AddLedgerInfrastructure(db.ConnectionString).BuildServiceProvider();

        await services.MigrateLedgerDatabaseAsync();

        var client = ClientId.New();
        await services.GetRequiredService<ILedgerStore>().PostAsync("dep", JournalEntries.Deposit(client, Money.Pln(5m)));
        var balance = Assert.Single(await services.GetRequiredService<ILedgerQueries>().ClientBalancesAsync(client));
        Assert.Equal(5m, balance.Balance);
        Assert.Null(await services.GetRequiredService<IClientRepository>().FindByEmailAsync(new EmailAddress("a@b.pl")));
        Assert.Same(TimeProvider.System, services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task Health_check_named_database_sees_whether_the_database_answers()
    {
        await using var db = await postgres.CreateDatabaseAsync();

        var healthy = await CheckAsync(db.ConnectionString);
        var unreachable = await CheckAsync("Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2");

        Assert.Equal(HealthStatus.Healthy, healthy.Entries["database"].Status);
        Assert.Equal(HealthStatus.Unhealthy, unreachable.Entries["database"].Status);
        Assert.Equal("cannot reach the ledger database", unreachable.Entries["database"].Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Requires_a_connection_string(string? connectionString) =>
        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddLedgerInfrastructure(connectionString!));

    [Fact]
    public async Task Migration_needs_a_service_provider() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => LedgerInfrastructure.MigrateLedgerDatabaseAsync(null!));

    private static async Task<HealthReport> CheckAsync(string connectionString)
    {
        var services = new ServiceCollection().AddLedgerInfrastructure(connectionString).AddLogging();
        services.AddHealthChecks().AddLedgerDatabaseCheck();
        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
    }

    [Fact]
    public void Exchange_client_needs_an_address_and_a_positive_timeout()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddParkietExchange(null!, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddParkietExchange(new Uri("http://x/"), TimeSpan.Zero));
    }
}
