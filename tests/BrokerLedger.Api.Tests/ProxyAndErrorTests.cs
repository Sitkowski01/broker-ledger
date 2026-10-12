using System.Net;
using System.Net.Http.Json;

namespace BrokerLedger.Api.Tests;

/// <summary>Adres klienta za reverse proxy, odpowiedzi bledow bez ciala i start z bledna konfiguracja.</summary>
[Collection(SharedApi.Name)]
public sealed class ProxyAndErrorTests(BrokerApi api)
{
    private static readonly IPAddress Proxy = IPAddress.Parse("10.0.0.5");

    [Fact]
    public async Task Behind_a_trusted_proxy_each_client_address_has_its_own_login_limit()
    {
        using var app = api.FromAddress(Proxy, ("ReverseProxy:KnownProxies:0", "10.0.0.5"), ("RateLimits:AuthPerMinute", "1"));
        using var http = app.CreateClient();

        var first = await LoginFrom(http, "203.0.113.1");
        var otherClient = await LoginFrom(http, "203.0.113.2");
        var firstAgain = await LoginFrom(http, "203.0.113.1");

        Assert.Equal(HttpStatusCode.Unauthorized, first);
        Assert.Equal(HttpStatusCode.Unauthorized, otherClient);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstAgain);
    }

    [Fact]
    public async Task Untrusted_sender_cannot_dodge_the_limit_with_a_forged_header()
    {
        // Ten sam nadawca podaje rozne X-Forwarded-For — nie jest zaufanym proxy, wiec naglowek sie nie liczy.
        using var app = api.FromAddress(IPAddress.Parse("198.51.100.7"), ("RateLimits:AuthPerMinute", "1"));
        using var http = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFrom(http, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFrom(http, "203.0.113.2"));
    }

    [Fact]
    public async Task Missing_token_is_answered_with_problem_json()
    {
        using var http = api.CreateClient();

        var response = await http.GetAsync(new Uri("/api/me/balances", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("ConnectionStrings:Ledger", "", "ConnectionStrings:Ledger is not configured")]
    [InlineData("Jwt:SigningKey", "too-short", "a SigningKey of at least 32 bytes")]
    [InlineData("RateLimits:AuthPerMinute", "0", "RateLimits must be positive")]
    [InlineData("Exchange:BaseUrl", "", "Exchange:BaseUrl and a positive Exchange:Timeout are required")]
    [InlineData("Exchange:Timeout", "00:00:00", "Exchange:BaseUrl and a positive Exchange:Timeout are required")]
    [InlineData("Orders:Reconciler:Interval", "00:00:00", "Orders:Reconciler needs a positive Interval and BatchSize")]
    [InlineData("Orders:Reconciler:MinAge", "00:00:00.200", "Orders:Reconciler:MinAge must be longer than Exchange:Timeout")]
    [InlineData("Orders:Settlement:Interval", "00:00:00", "Orders:Settlement needs a positive Interval and BatchSize")]
    [InlineData("Exchange:BaseUrl", "http://gw/parkiet", "Exchange:BaseUrl must be an absolute URL ending with")]
    [InlineData("Exchange:BaseUrl", "parkiet/", "Exchange:BaseUrl must be an absolute URL ending with")]
    [InlineData("ExchangeRates:BaseUrl", "", "ExchangeRates:BaseUrl must be an absolute URL ending with")]
    [InlineData("ExchangeRates:BaseUrl", "http://nbp/api", "ExchangeRates:BaseUrl must be an absolute URL ending with")]
    [InlineData("ExchangeRates:BaseUrl", "nbp/", "ExchangeRates:BaseUrl must be an absolute URL ending with")]
    [InlineData("ExchangeRates:AttemptTimeout", "00:00:00", "ExchangeRates needs a positive AttemptTimeout, CacheFor and RetryAfter")]
    [InlineData("ExchangeRates:CacheFor", "00:00:00", "ExchangeRates needs a positive AttemptTimeout, CacheFor and RetryAfter")]
    [InlineData("ExchangeRates:RetryAfter", "00:00:00", "ExchangeRates needs a positive AttemptTimeout, CacheFor and RetryAfter")]
    [InlineData("ExchangeRates:Currencies:0", "eur", "currency code must be 3 upper-case letters")]
    public void Bad_configuration_names_the_problem(string key, string value, string message)
    {
        using var broken = api.With((key, value));

        var ex = Assert.ThrowsAny<Exception>(() => broken.CreateClient());

        Assert.Contains(message, ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ExchangeEvents:Topic", " ", "topic")]
    [InlineData("ExchangeEvents:GroupId", "", "groupId")]
    [InlineData("ExchangeEvents:RetryDelay", "00:00:00", "retryDelay")]
    public void Kafka_settings_are_checked_only_when_kafka_is_configured(string key, string value, string parameter)
    {
        using var withKafka = api.With(("ExchangeEvents:BootstrapServers", "localhost:1"), (key, value));
        using var withoutKafka = api.With((key, value));

        var ex = Assert.ThrowsAny<ArgumentException>(() => withKafka.CreateClient());

        Assert.Equal(parameter, ex.ParamName);
        withoutKafka.CreateClient().Dispose();
    }

    private static async Task<HttpStatusCode> LoginFrom(HttpClient http, string clientAddress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "x@example.com", password = "guess" }),
        };
        request.Headers.Add("X-Forwarded-For", clientAddress);
        return (await http.SendAsync(request)).StatusCode;
    }
}
