using BrokerLedger.Domain;
using Microsoft.AspNetCore.Identity;

namespace BrokerLedger.Api.Auth;

internal sealed record RegisterRequest(string? Email, string? Password);

internal sealed record LoginRequest(string? Email, string? Password);

internal static class AuthEndpoints
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    // Hash, ktory sprawdzamy, gdy klienta nie ma — zeby odpowiedz "nie ma takiego e-maila"
    // trwala tyle samo co "zle haslo" i nie zdradzala, kto ma konto.
    // Stryker disable once String : tresc nie ma znaczenia, liczy sie tylko koszt sprawdzenia hasha
    private static readonly string DummyHash =
        new PasswordHasher<ClientAccount>().HashPassword(null!, "not-a-real-password");

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting(RateLimits.Auth);
        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/login", LoginAsync);
        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, IClientRepository clients, IPasswordHasher<ClientAccount> hasher, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var email = TryEmail(request.Email, errors);
        if (request.Password is not { Length: >= MinPasswordLength and <= MaxPasswordLength })
        {
            errors["password"] = [$"password must be {MinPasswordLength}-{MaxPasswordLength} characters"];
        }
        if (email is null || errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var id = ClientId.New();
        var hash = hasher.HashPassword(null!, request.Password!);
        if (!await clients.TryAddAsync(new ClientAccount(id, email, hash), ct))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "E-mail already registered");
        }
        return Results.Json(new { clientId = id.Value }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, IClientRepository clients, IPasswordHasher<ClientAccount> hasher, TokenIssuer tokens,
        CancellationToken ct)
    {
        var email = TryEmail(request.Email, []);
        var client = email is null ? null : await clients.FindByEmailAsync(email, ct);
        // Stryker disable once String : brak hasla i tak nie pasuje do zadnego hasha
        var result = hasher.VerifyHashedPassword(client!, client?.PasswordHash ?? DummyHash, request.Password ?? "");
        if (client is null || result == PasswordVerificationResult.Failed)
        {
            // Ta sama odpowiedz dla zlego e-maila i zlego hasla.
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid e-mail or password");
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Haslo poprawne, ale hash ze starszymi parametrami — to jedyny moment, gdy znamy haslo,
            // wiec tu podnosimy go do obecnych ustawien.
            await clients.UpdatePasswordHashAsync(client.Id, hasher.HashPassword(client, request.Password!), ct);
        }

        var token = tokens.Issue(client.Id);
        return Results.Ok(new { accessToken = token.Token, expiresAt = token.ExpiresAt });
    }

    private static EmailAddress? TryEmail(string? value, Dictionary<string, string[]> errors)
    {
        try
        {
            return new EmailAddress(value!);
        }
        catch (ArgumentException)
        {
            errors["email"] = ["a valid e-mail address is required"];
            return null;
        }
    }
}
