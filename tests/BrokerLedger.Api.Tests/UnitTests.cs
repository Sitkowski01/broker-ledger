using System.Security.Claims;
using BrokerLedger.Api.Accounts;
using BrokerLedger.Api.Auth;
using BrokerLedger.Api.Errors;
using BrokerLedger.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BrokerLedger.Api.Tests;

/// <summary>Male klocki API sprawdzane bez serwera — szybciej i dokladniej niz przez HTTP.</summary>
public class UnitTests
{
    private static JwtOptions ValidJwt() => new()
    {
        Issuer = "issuer",
        Audience = "audience",
        SigningKey = new string('k', 32),
        Lifetime = TimeSpan.FromMinutes(5),
    };

    [Fact]
    public void Jwt_options_require_every_part()
    {
        Assert.True(ValidJwt().IsValid());
        Assert.False(new JwtOptions().IsValid());

        var noIssuer = ValidJwt();
        noIssuer.Issuer = " ";
        var noAudience = ValidJwt();
        noAudience.Audience = "";
        var shortKey = ValidJwt();
        shortKey.SigningKey = new string('k', 31);
        var noLifetime = ValidJwt();
        noLifetime.Lifetime = TimeSpan.Zero;

        Assert.All([noIssuer, noAudience, shortKey, noLifetime], o => Assert.False(o.IsValid()));
    }

    [Fact]
    public void Default_jwt_options_are_invalid_part_by_part()
    {
        // Domyslne wartosci nie moga przypadkiem przejsc walidacji, gdy w konfiguracji czegos brakuje.
        var issuerMissing = ValidJwt();
        issuerMissing.Issuer = new JwtOptions().Issuer;
        var audienceMissing = ValidJwt();
        audienceMissing.Audience = new JwtOptions().Audience;

        Assert.False(issuerMissing.IsValid());
        Assert.False(audienceMissing.IsValid());
        Assert.Equal(TimeSpan.FromHours(1), new JwtOptions().Lifetime);
    }

    [Fact]
    public void Issued_token_carries_the_client_and_expiry()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        var client = ClientId.New();

        var token = new TokenIssuer(Options.Create(ValidJwt()), clock).Issue(client);

        var jwt = new JsonWebToken(token.Token);
        Assert.Equal((client.ToString(), "issuer"), (jwt.Subject, jwt.Issuer));
        Assert.Equal(clock.GetUtcNow().AddMinutes(5), token.ExpiresAt);
        Assert.Equal(token.ExpiresAt.UtcDateTime, jwt.ValidTo);
        Assert.Throws<ArgumentNullException>(() => new TokenIssuer(Options.Create(ValidJwt()), clock).Issue(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Principal_without_a_valid_client_id_is_a_bug_not_a_client(string? sub)
    {
        var claims = sub is null ? [] : new[] { new Claim("sub", sub) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var ex = Assert.Throws<InvalidOperationException>(() => user.ClientId());

        Assert.Equal("authenticated user has no client id", ex.Message);
    }

    [Fact]
    public void Principal_with_a_client_id_gives_it_back()
    {
        var id = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "test"));

        Assert.Equal(new ClientId(id), user.ClientId());
    }

    [Theory]
    [InlineData("250.40", true, "250.40")]
    [InlineData("7", true, "7")]
    [InlineData("1e3", false, "0")]
    [InlineData("-5", false, "0")]
    [InlineData(" 5", false, "0")]
    [InlineData(null, false, "0")]
    public void Amounts_accept_only_plain_decimals(string? text, bool ok, string expected)
    {
        Assert.Equal(ok, Amounts.TryParse(text, out var amount));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), amount);
    }

    [Fact]
    public void Amounts_are_printed_with_the_assets_precision()
    {
        Assert.Equal("250.40", Amounts.Format(250.4m, Asset.Cash(Currency.Pln)));
        Assert.Equal("15", Amounts.Format(15m, Asset.Shares("CDR")));
        Assert.Throws<ArgumentNullException>(() => Amounts.Format(1m, null!));
    }

    [Fact]
    public async Task Unknown_errors_and_broker_shortfalls_stay_500()
    {
        var handler = new LedgerExceptionHandler(new NoProblemWriter());
        var http = new DefaultHttpContext();

        var bug = await handler.TryHandleAsync(http, new InvalidOperationException("bug"), CancellationToken.None);
        var brokerShortfall = await handler.TryHandleAsync(http,
            new InsufficientBalanceException(AccountId.Bank(Currency.Pln), 0m, 1m), CancellationToken.None);

        Assert.False(bug);
        Assert.False(brokerShortfall);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
    }

    [Fact]
    public async Task Conflict_has_a_clear_title()
    {
        var writer = new NoProblemWriter();
        var http = new DefaultHttpContext();

        var handled = await new LedgerExceptionHandler(writer)
            .TryHandleAsync(http, new IdempotencyConflictException("k"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        Assert.Equal("Idempotency-Key was already used for a different operation", writer.Last!.ProblemDetails.Title);
    }

    private sealed class NoProblemWriter : IProblemDetailsService
    {
        public ProblemDetailsContext? Last { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context) => throw new NotSupportedException();

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Last = context;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Concurrent_change_of_an_order_is_a_conflict()
    {
        var writer = new NoProblemWriter();
        var http = new DefaultHttpContext();

        var handled = await new LedgerExceptionHandler(writer)
            .TryHandleAsync(http, new OrderConcurrencyException(OrderId.New()), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        Assert.Equal("Order was changed at the same time, read it again", writer.Last!.ProblemDetails.Title);
    }
}
