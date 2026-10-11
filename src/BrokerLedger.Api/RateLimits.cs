using System.Threading.RateLimiting;
using BrokerLedger.Api.Auth;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BrokerLedger.Api;

/// <summary>Sekcja <c>RateLimits</c>: ile zadan na minute.</summary>
internal sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Logowanie i rejestracja, na adres IP — hamuje zgadywanie hasel.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Operacje na koncie, na klienta — jeden klient nie zatka API innym.</summary>
    public int ClientPerMinute { get; set; } = 120;
}

internal static class RateLimits
{
    public const string Auth = "auth";
    public const string Client = "client";

    public static IServiceCollection AddBrokerRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.Section))
            .Validate(o => o is { AuthPerMinute: > 0, ClientPerMinute: > 0 }, "RateLimits must be positive")
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                }
                await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests")
                    .ExecuteAsync(context.HttpContext);
            };

            limiter.AddPolicy(Auth, http => PerMinute(
                // Stryker disable once String : nazwa wspolnego koszyka bez adresu nie ma znaczenia
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown", Options(http).AuthPerMinute));
            limiter.AddPolicy(Client, http => PerMinute(
                // Autoryzacja odcina anonimowych wczesniej, wiec tu klient jest zawsze znany.
                http.User.ClientId().ToString(),
                Options(http).ClientPerMinute));
        });
        return services;
    }

    private static RateLimitOptions Options(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    private static RateLimitPartition<string> PerMinute(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}
