using System.Net;
using BrokerLedger.Api;
using BrokerLedger.Api.Accounts;
using BrokerLedger.Api.Auth;
using BrokerLedger.Api.Errors;
using BrokerLedger.Api.Orders;
using BrokerLedger.Api.Portfolios;
using BrokerLedger.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Ledger");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Ledger is not configured");
}
builder.Services.AddLedgerInfrastructure(connectionString);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<LedgerExceptionHandler>();
builder.Services.AddBrokerAuth(builder.Configuration);
builder.Services.AddBrokerRateLimits(builder.Configuration);
builder.Services.AddBrokerOrders(builder.Configuration);
builder.Services.AddBrokerPortfolio(builder.Configuration);
builder.Services.AddHealthChecks().AddLedgerDatabaseCheck();
// Za reverse proxy adres klienta jest w X-Forwarded-For. Ufamy tylko proxy z konfiguracji
// (domyslnie localhost) — inaczej kazdy moglby podac dowolny adres i ominac limit logowan.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

// Zla konfiguracja ma zatrzymac start, zanim migracje cokolwiek zmienia w bazie.
app.Services.GetRequiredService<IStartupValidator>().Validate();
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateLedgerDatabaseAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
// Stryker disable once Statement : ASP.NET Core dodaje uwierzytelnianie sam; jawnie dla czytelnosci kolejnosci
app.UseAuthentication();
app.UseAuthorization();
// Po uwierzytelnieniu, bo limit operacji na koncie liczy sie na klienta z tokenu.
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapOrderEndpoints();
app.MapPortfolioEndpoints();

await app.RunAsync();
