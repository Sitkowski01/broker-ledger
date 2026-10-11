using BrokerLedger.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BrokerLedger.Api.Auth;

internal static class AuthSetup
{
    public static IServiceCollection AddBrokerAuth(this IServiceCollection services, IConfiguration configuration)
    {
        // Zla konfiguracja ma wywalic start aplikacji, a nie pierwsze logowanie.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.Section))
            .Validate(o => o.IsValid(), "Jwt: Issuer, Audience, a SigningKey of at least 32 bytes and a positive Lifetime are required")
            .ValidateOnStart();

        services.AddSingleton<TokenIssuer>();
        services.AddSingleton<IPasswordHasher<ClientAccount>, PasswordHasher<ClientAccount>>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                // Bez mapowania "sub" zostaje "sub", a nie dlugim URI z WS-Federation.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = jwt.Value.Key(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
        services.AddAuthorization();
        return services;
    }
}
