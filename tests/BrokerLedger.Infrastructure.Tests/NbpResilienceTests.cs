using System.Net;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Kursy z DI: prawdziwy potok odpornosci (ponowienia, limit proby) przed atrapa serwera NBP.</summary>
public sealed class NbpResilienceTests
{
    private static ServiceProvider Services(FakeNbp nbp, TimeSpan? attemptTimeout = null)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System);
        services.AddNbpExchangeRates(new Uri("https://nbp.test/"), attemptTimeout ?? TimeSpan.FromSeconds(5),
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(1));
        services.AddHttpClient(LedgerInfrastructure.NbpHttpClient).ConfigurePrimaryHttpMessageHandler(() => nbp);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Server_errors_are_retried_until_a_table_arrives()
    {
        var nbp = new FakeNbp().Then(FakeNbp.Status(HttpStatusCode.ServiceUnavailable), FakeNbp.Status(HttpStatusCode.InternalServerError), FakeNbp.Ok);
        await using var services = Services(nbp);

        var snapshot = await services.GetRequiredService<IExchangeRates>().CurrentAsync();

        Assert.Equal(("198/A/NBP/2026", false), (snapshot!.Table.Number, snapshot.Stale));
        Assert.Equal(3, nbp.Calls);
    }

    [Fact]
    public async Task Hanging_attempt_is_cut_off_and_retried()
    {
        var nbp = new FakeNbp().Then(FakeNbp.Hang, FakeNbp.Ok);
        await using var services = Services(nbp, attemptTimeout: TimeSpan.FromMilliseconds(300));

        var snapshot = await services.GetRequiredService<IExchangeRates>().CurrentAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(2, nbp.Calls);
    }

    [Fact]
    public async Task Api_that_stays_down_gives_up_after_three_attempts()
    {
        var nbp = new FakeNbp().Then(FakeNbp.Status(HttpStatusCode.ServiceUnavailable));
        await using var services = Services(nbp);
        var rates = services.GetRequiredService<IExchangeRates>();

        Assert.Null(await rates.CurrentAsync());
        Assert.Null(await rates.CurrentAsync());
        Assert.Equal(3, nbp.Calls);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var nbp = new FakeNbp().Then(FakeNbp.Status(HttpStatusCode.NotFound));
        await using var services = Services(nbp);

        Assert.Null(await services.GetRequiredService<IExchangeRates>().CurrentAsync());
        Assert.Equal(1, nbp.Calls);
    }

    [Fact]
    public async Task Rates_are_one_shared_cache()
    {
        var nbp = new FakeNbp();
        await using var services = Services(nbp);

        await services.GetRequiredService<IExchangeRates>().CurrentAsync();
        await services.GetRequiredService<IExchangeRates>().CurrentAsync();

        Assert.Same(services.GetRequiredService<IExchangeRates>(), services.GetRequiredService<IExchangeRates>());
        Assert.Equal(1, nbp.Calls);
    }

    [Fact]
    public void Settings_must_be_given()
    {
        var services = new ServiceCollection();
        var url = new Uri("https://nbp.test/");
        var second = TimeSpan.FromSeconds(1);

        Assert.Throws<ArgumentNullException>(() => services.AddNbpExchangeRates(null!, second, second, second));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddNbpExchangeRates(url, TimeSpan.Zero, second, second));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddNbpExchangeRates(url, second, TimeSpan.Zero, second));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddNbpExchangeRates(url, second, second, TimeSpan.Zero));
    }
}
