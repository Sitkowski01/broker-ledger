using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BrokerLedger.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BrokerLedger.Api.Tests;

[Collection(SharedApi.Name)]
public sealed class AuthTests(BrokerApi api)
{
    private readonly HttpClient _http = api.CreateClient();
    private readonly string _email = $"{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Registered_client_can_log_in_and_see_its_balances()
    {
        var register = await _http.PostAsJsonAsync("/api/auth/register", new { email = _email, password = "secret-password" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.True(Guid.TryParse((await register.JsonAsync()).GetProperty("clientId").GetString(), out _));

        var login = await _http.PostAsJsonAsync("/api/auth/login", new { email = _email.ToUpperInvariant(), password = "secret-password" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.JsonAsync();
        Assert.True(body.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        var balances = await _http.GetAsync(new Uri("/api/me/balances", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, balances.StatusCode);
        Assert.Equal(0, (await balances.JsonAsync()).GetArrayLength());
    }

    [Fact]
    public async Task Same_email_in_another_case_is_already_taken()
    {
        await _http.PostAsJsonAsync("/api/auth/register", new { email = _email, password = "secret-password" });

        var again = await _http.PostAsJsonAsync("/api/auth/register", new { email = " " + _email.ToUpperInvariant(), password = "other-password" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("E-mail already registered", (await again.JsonAsync()).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("not-an-email", "secret-password", "email")]
    [InlineData(null, "secret-password", "email")]
    [InlineData("a@b.pl", "short", "password")]
    [InlineData("a@b.pl", null, "password")]
    public async Task Registration_validates_input(string? email, string? password, string field)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/register", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_look_the_same()
    {
        await _http.PostAsJsonAsync("/api/auth/register", new { email = _email, password = "secret-password" });

        var wrongPassword = await _http.PostAsJsonAsync("/api/auth/login", new { email = _email, password = "wrong-password" });
        var unknownEmail = await _http.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "secret-password" });
        var garbage = await _http.PostAsJsonAsync("/api/auth/login", new { email = "garbage", password = (string?)null });

        Assert.All([wrongPassword, unknownEmail, garbage], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(
            (await wrongPassword.JsonAsync()).GetProperty("title").GetString(),
            (await unknownEmail.JsonAsync()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Old_password_hash_is_upgraded_on_login()
    {
        // Hash w starym formacie (ASP.NET Identity v2) — jakby klient zalozyl konto przed zmiana ustawien.
        var clients = api.Services.GetRequiredService<IClientRepository>();
        var oldHasher = new PasswordHasher<ClientAccount>(Options.Create(
            new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2 }));
        var register = await _http.PostAsJsonAsync("/api/auth/register", new { email = _email, password = "secret-password" });
        var id = new ClientId(Guid.Parse((await register.JsonAsync()).GetProperty("clientId").GetString()!));
        var oldHash = oldHasher.HashPassword(null!, "secret-password");
        await clients.UpdatePasswordHashAsync(id, oldHash);

        var login = await _http.PostAsJsonAsync("/api/auth/login", new { email = _email, password = "secret-password" });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var stored = (await clients.FindByEmailAsync(new EmailAddress(_email)))!.PasswordHash;
        Assert.NotEqual(oldHash, stored);
        Assert.Equal(1, Convert.FromBase64String(stored)[0]);  // 0x01 = format v3
    }

    [Fact]
    public async Task Account_endpoints_require_a_token()
    {
        var response = await _http.GetAsync(new Uri("/api/me/balances", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-key-that-is-also-at-least-32-bytes", "broker-ledger", 60)]
    [InlineData(BrokerApi.SigningKey, "someone-else", 60)]
    [InlineData(BrokerApi.SigningKey, "broker-ledger", -5)]
    public async Task Forged_or_expired_tokens_are_rejected(string key, string issuer, int minutesToExpiry)
    {
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = "broker-ledger-clients",
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            IssuedAt = now.AddMinutes(-10),
            NotBefore = now.AddMinutes(-10),
            Expires = now.AddMinutes(minutesToExpiry),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        });
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.GetAsync(new Uri("/api/me/balances", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
