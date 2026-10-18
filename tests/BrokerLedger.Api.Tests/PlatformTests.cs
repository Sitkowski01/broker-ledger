using System.Net;
using System.Net.Http.Json;
using NetArchTest.Rules;

namespace BrokerLedger.Api.Tests;

/// <summary>To, co dookola endpointow: limity zadan, health check, start z zla konfiguracja, architektura.</summary>
[Collection(SharedApi.Name)]
public sealed class PlatformTests(BrokerApi api)
{
    [Fact]
    public async Task OpenApi_describes_the_endpoints_and_the_token_and_docs_show_it()
    {
        using var http = api.CreateClient();

        var document = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/openapi/v1.json");
        var docs = await http.GetAsync(new Uri("/docs", UriKind.Relative));

        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/me/orders", paths);
        Assert.Contains("/api/me/events", paths);
        Assert.Contains("/api/me/performance", paths);
        var info = document.GetProperty("info");
        Assert.Equal(("broker-ledger", "v1"), (info.GetProperty("title").GetString(), info.GetProperty("version").GetString()));
        Assert.StartsWith("Dom maklerski przed gielda Parkiet", info.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", info.GetProperty("description").GetString(), StringComparison.Ordinal);
        var components = document.GetProperty("components");
        var bearer = components.GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal(("http", "bearer", "JWT"),
            (bearer.GetProperty("type").GetString(), bearer.GetProperty("scheme").GetString(), bearer.GetProperty("bearerFormat").GetString()));
        Assert.Contains("/api/auth/login", bearer.GetProperty("description").GetString(), StringComparison.Ordinal);
        // Wymaganie tokenu dla calego dokumentu — Swagger UI po Authorize wysyla go z kazdym zapytaniem.
        Assert.True(Assert.Single(document.GetProperty("security").EnumerateArray()).TryGetProperty("Bearer", out _));
        // Schematy cial zapytan zostaja obok dopisanego schematu tokenu.
        Assert.True(components.GetProperty("schemas").EnumerateObject().Any());
        Assert.Equal(HttpStatusCode.OK, docs.StatusCode);
        Assert.Contains("swagger-ui-dist@5.32.15", await docs.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_attempts_are_limited_per_address()
    {
        using var limited = api.With(("RateLimits:AuthPerMinute", "3"));
        using var http = limited.CreateClient();

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            responses.Add(await http.PostAsJsonAsync("/api/auth/login", new { email = "x@example.com", password = "guess-" + i }));
        }

        Assert.All(responses.Take(3), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[3].StatusCode);
        Assert.True(responses[3].Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("Too many requests", (await responses[3].JsonAsync()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Account_requests_are_limited_per_client_not_globally()
    {
        using var limited = api.With(("RateLimits:ClientPerMinute", "2"));
        using var alice = await Clients.RegisterAndLoginAsync(limited);
        using var bob = await Clients.RegisterAndLoginAsync(limited);

        var aliceStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            aliceStatuses.Add((await alice.GetAsync(new Uri("/api/me/balances", UriKind.Relative))).StatusCode);
        }
        var bobStatus = (await bob.GetAsync(new Uri("/api/me/balances", UriKind.Relative))).StatusCode;

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], aliceStatuses);
        Assert.Equal(HttpStatusCode.OK, bobStatus);
    }

    [Fact]
    public async Task Health_checks_the_database()
    {
        using var http = api.CreateClient();

        var response = await http.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_is_unhealthy_without_the_database()
    {
        using var broken = api.With(("ConnectionStrings:Ledger", "Host=localhost;Port=1;Database=nope;Username=x;Password=x;Timeout=2"),
            ("Database:MigrateOnStartup", "false"));
        using var http = broken.CreateClient();

        var response = await http.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("Jwt:SigningKey", "too-short")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("RateLimits:ClientPerMinute", "0")]
    public void App_refuses_to_start_with_bad_configuration(string key, string value)
    {
        using var broken = api.With((key, value));

        var ex = Assert.ThrowsAny<Exception>(() => broken.CreateClient());

        Assert.Contains("Microsoft.Extensions.Options.OptionsValidationException", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_configuration_stops_the_app_before_it_touches_the_database()
    {
        var database = $"never_{Guid.NewGuid():N}";
        using var broken = api.With(("ConnectionStrings:Ledger", api.ConnectionString(database)), ("Jwt:SigningKey", "too-short"));

        Assert.ThrowsAny<Exception>(() => broken.CreateClient());

        // Migracja zalozylaby te baze — skoro jej nie ma, start zatrzymal sie przed migracja.
        await using var connection = new Npgsql.NpgsqlConnection(api.ConnectionString("postgres"));
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand($"SELECT count(*) FROM pg_database WHERE datname = '{database}'", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public void Web_layer_talks_to_the_ledger_only_through_domain_ports()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
