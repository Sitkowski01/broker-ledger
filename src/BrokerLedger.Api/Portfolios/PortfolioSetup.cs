using BrokerLedger.Domain;
using BrokerLedger.Infrastructure;

namespace BrokerLedger.Api.Portfolios;

/// <summary>Sekcja <c>ExchangeRates</c>: skad brac kursy walut i w jakich walutach pokazac wartosc portfela.</summary>
internal sealed class ExchangeRatesOptions
{
    public const string Section = "ExchangeRates";

    public Uri? BaseUrl { get; set; }

    /// <summary>Limit jednej proby — po nim potok ponawia, a na koniec portfel bierze ostatnia znana tabele.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>NBP publikuje tabele raz dziennie; godzina w pamieci to wciaz dzisiejszy kurs.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Przerwa po nieudanej probie: w tym czasie portfel nie czeka na lezace API.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(1);

    public List<string> Currencies { get; set; } = [];
}

internal static class PortfolioSetup
{
    public static IServiceCollection AddBrokerPortfolio(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ExchangeRatesOptions.Section).Get<ExchangeRatesOptions>() ?? new ExchangeRatesOptions();
        // Ten sam powod co przy gieldzie: bez ukosnika HttpClient ucialby ostatni segment adresu.
        if (options.BaseUrl is not { IsAbsoluteUri: true } || !options.BaseUrl.AbsolutePath.EndsWith('/'))
        {
            throw new InvalidOperationException($"ExchangeRates:BaseUrl must be an absolute URL ending with '/', was '{options.BaseUrl}'");
        }
        if (options.AttemptTimeout <= TimeSpan.Zero || options.CacheFor <= TimeSpan.Zero || options.RetryAfter <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("ExchangeRates needs a positive AttemptTimeout, CacheFor and RetryAfter");
        }
        services.AddNbpExchangeRates(options.BaseUrl, options.AttemptTimeout, options.CacheFor, options.RetryAfter);
        // Nieznany kod waluty ma zatrzymac start, a nie wyjsc dopiero w pierwszym portfelu.
        services.AddSingleton(new PortfolioCurrencies(options.Currencies.Select(code => new Currency(code)).Distinct().ToList()));
        return services;
    }
}

/// <summary>Waluty, w ktorych portfel pokazuje wartosc obok zlotowek.</summary>
internal sealed record PortfolioCurrencies(IReadOnlyList<Currency> Codes);
