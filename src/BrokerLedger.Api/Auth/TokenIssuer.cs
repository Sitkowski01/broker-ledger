using System.Security.Claims;
using BrokerLedger.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BrokerLedger.Api.Auth;

internal sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Wydaje JWT, w ktorym <c>sub</c> to id klienta — po nim API wie, czyje konta pokazac.</summary>
internal sealed class TokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(ClientId client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now + jwt.Lifetime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, client.ToString())]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.Key(), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expiresAt);
    }
}

internal static class ClientClaims
{
    /// <summary>Id klienta z tokenu. Endpointy pod autoryzacja maja go zawsze — brak to blad konfiguracji.</summary>
    public static ClientId ClientId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) && id != Guid.Empty
            ? new ClientId(id)
            : throw new InvalidOperationException("authenticated user has no client id");

    /// <summary>Do kiedy wazny jest token (<c>exp</c>, sekundy od epoki) — kazdy wydany przez TokenIssuer go ma.</summary>
    public static DateTimeOffset ExpiresAt(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Exp), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : throw new InvalidOperationException("authenticated user has no token expiry");
}
