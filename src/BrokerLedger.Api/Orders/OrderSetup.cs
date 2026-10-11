using BrokerLedger.Domain;
using BrokerLedger.Infrastructure;

namespace BrokerLedger.Api.Orders;

/// <summary>Sekcja <c>Exchange</c>: adres gieldy Parkiet i ile czekac na jej odpowiedz.</summary>
internal sealed class ExchangeOptions
{
    public const string Section = "Exchange";

    public Uri? BaseUrl { get; set; }

    /// <summary>Po tym czasie zlecenie jest "nieznane" i wyjasnia je reconciler.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Sekcja <c>Orders:Reconciler</c>.</summary>
internal sealed class ReconcilerOptions
{
    public const string Section = "Orders:Reconciler";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Zlecen zmienionych pozniej nie ruszamy — zajmuje sie nimi jeszcze zadanie klienta.</summary>
    public TimeSpan MinAge { get; set; } = TimeSpan.FromSeconds(15);

    public int BatchSize { get; set; } = 50;

    public bool IsValid() => Interval > TimeSpan.Zero && MinAge >= TimeSpan.Zero && BatchSize > 0;
}

internal static class OrderSetup
{
    public static IServiceCollection AddBrokerOrders(this IServiceCollection services, IConfiguration configuration)
    {
        var exchange = configuration.GetSection(ExchangeOptions.Section).Get<ExchangeOptions>() ?? new ExchangeOptions();
        if (exchange.BaseUrl is null || exchange.Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Exchange:BaseUrl and a positive Exchange:Timeout are required");
        }
        // Bez ukosnika na koncu HttpClient ucialby ostatni segment sciezki ("http://gw/parkiet" + "api/..." = "http://gw/api/..."),
        // a 404 z niewlasciwego adresu wygladaloby jak odmowa gieldy — i zwalnialoby srodki.
        if (!exchange.BaseUrl.IsAbsoluteUri || !exchange.BaseUrl.AbsolutePath.EndsWith('/'))
        {
            throw new InvalidOperationException($"Exchange:BaseUrl must be an absolute URL ending with '/', was '{exchange.BaseUrl}'");
        }
        services.AddParkietExchange(exchange.BaseUrl, exchange.Timeout);

        services.AddSingleton(Commission.Standard);
        services.AddTransient<OrderSaga>();

        services.AddOptions<ReconcilerOptions>()
            .Bind(configuration.GetSection(ReconcilerOptions.Section))
            .Validate(o => o.IsValid(), "Orders:Reconciler needs a positive Interval and BatchSize")
            // Reconciler nie moze brac zlecen, ktore zadanie klienta wciaz wysyla.
            .Validate(o => o.MinAge > exchange.Timeout, "Orders:Reconciler:MinAge must be longer than Exchange:Timeout")
            .ValidateOnStart();
        services.AddHostedService<OrderReconciler>();
        return services;
    }
}
