using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace BrokerLedger.Api.Tests;

/// <summary>
/// Cala aplikacja w pamieci (TestServer) na prawdziwym PostgreSQL w Dockerze. Migracje robi sama aplikacja
/// przy starcie — tak jak lokalnie. Testy rozdzielaja sie unikalnymi adresami e-mail.
/// </summary>
public sealed class BrokerApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "test-signing-key-at-least-32-bytes-long";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Pierwsze zapytanie stawia host i robi migracje — zanim testy zaczna sie scigac.
        using var client = CreateClient();
        (await client.GetAsync(new Uri("/health", UriKind.Relative))).EnsureSuccessStatusCode();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Polaczenie do innej bazy w tym samym kontenerze (moze jeszcze nie istniec).</summary>
    public string ConnectionString(string database) =>
        new Npgsql.NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    /// <summary>Ta sama baza, ale wlasne ustawienia (np. niskie limity) i wlasny stan limitera.</summary>
    public WebApplicationFactory<Program> With(params (string Key, string Value)[] settings) =>
        WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ledger", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        // Wszystkie testy ida z jednego "adresu IP" — limit logowan podniesiony, sprawdza go osobny test.
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:ClientPerMinute", "100000");
    }
}

[CollectionDefinition(Name)]
public sealed class SharedApi : ICollectionFixture<BrokerApi>
{
    public const string Name = "api";
}

/// <summary>Klient HTTP zalogowany jako nowy, swiezo zarejestrowany klient brokera.</summary>
internal static class Clients
{
    public static async Task<HttpClient> RegisterAndLoginAsync(WebApplicationFactory<Program> api)
    {
        var http = api.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.com";
        var credentials = new { email, password = "correct horse battery" };
        (await http.PostAsJsonAsync("/api/auth/register", credentials)).EnsureSuccessStatusCode();
        var login = await http.PostAsJsonAsync("/api/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    public static Task<HttpResponseMessage> PostMoneyAsync(
        this HttpClient http, string path, string? idempotencyKey, string amount, string currency = "PLN")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { amount, currency }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return http.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
