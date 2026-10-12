using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using BrokerLedger.Api.Orders;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    /// <summary>Gielda, z ktora rozmawia broker w testach — w pamieci, z awariami na zyczenie.</summary>
    public FakeParkiet Parkiet { get; } = new();

    /// <summary>Jeden przebieg reconcilera — w testach wylaczonego, zeby wynik nie zalezal od zegara.</summary>
    internal async Task<int> ReconcileAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        return await Services.GetServices<IHostedService>().OfType<OrderReconciler>().Single().RunOnceAsync();
    }

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

    /// <summary>
    /// Jak <see cref="With"/>, ale zadania przychodza z podanego adresu IP. TestServer go nie ustawia,
    /// wiec robi to filtr przed reszta potoku — tam, gdzie w prawdziwym serwerze robi to Kestrel.
    /// </summary>
    public WebApplicationFactory<Program> FromAddress(IPAddress remote, params (string Key, string Value)[] settings) =>
        WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddress(remote)));
        });

    private sealed class RemoteAddress(IPAddress remote) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remote;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ledger", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        // Wszystkie testy ida z jednego "adresu IP" — limit logowan podniesiony, sprawdza go osobny test.
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:ClientPerMinute", "100000");
        builder.UseSetting("Exchange:BaseUrl", "http://parkiet.test/");
        builder.UseSetting("Orders:Reconciler:Enabled", "false");
        builder.UseSetting("Orders:Settlement:Enabled", "false");
        // Reconciler bierze tylko zlecenia starsze niz timeout gieldy — w testach oba sa krotkie.
        builder.UseSetting("Exchange:Timeout", "00:00:00.200");
        builder.UseSetting("Orders:Reconciler:MinAge", "00:00:00.300");
        builder.ConfigureServices(services =>
            services.AddHttpClient(nameof(IExchange)).ConfigurePrimaryHttpMessageHandler(Parkiet.Handler));
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

    /// <summary>Id klienta zalogowanego klientem HTTP — z tokenu, jak robi to API.</summary>
    public static ClientId ClientIdOf(HttpClient http) =>
        new(Guid.Parse(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(http.DefaultRequestHeaders.Authorization!.Parameter!).Subject));
}
