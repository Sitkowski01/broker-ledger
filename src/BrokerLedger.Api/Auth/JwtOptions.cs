using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BrokerLedger.Api.Auth;

/// <summary>Sekcja <c>Jwt</c> w konfiguracji. Klucz podpisu tylko z sekretow/zmiennych srodowiskowych, nigdy z repo.</summary>
internal sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    // Stryker disable once String : kazdy domyslny tekst krotszy niz 32 bajty jest tak samo odrzucany
    public string SigningKey { get; set; } = "";
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>HMAC-SHA256 potrzebuje klucza co najmniej 256-bitowego.</summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience)
        && Encoding.UTF8.GetByteCount(SigningKey) >= 32
        && Lifetime > TimeSpan.Zero;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));
}
