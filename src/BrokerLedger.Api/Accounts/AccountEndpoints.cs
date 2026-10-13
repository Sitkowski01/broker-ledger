using BrokerLedger.Api.Auth;
using BrokerLedger.Domain;
using Microsoft.AspNetCore.Mvc;

namespace BrokerLedger.Api.Accounts;

internal sealed record MoneyRequest(string? Amount, string? Currency);

internal static class AccountEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const int MaxIdempotencyKeyLength = 100;
    public const int DefaultHistoryPage = 20;

    /// <summary>Gielda Parkiet handluje w zlotowkach, wiec rachunki gotowkowe sa tylko w PLN.</summary>
    public static readonly IReadOnlyList<Currency> SupportedCurrencies = [Currency.Pln];

    public const string KeyError =
        "header Idempotency-Key with 1-100 printable ASCII characters is required";

    /// <summary>Klucz od klienta: 1-100 drukowalnych znakow ASCII (bez spacji).</summary>
    public static bool IsValidKey(string? key) =>
        // Stryker disable once Equality : pusty naglowek dochodzi jako null, wiec "Length >= 0" dziala tak samo
        key is { Length: > 0 and <= MaxIdempotencyKeyLength } && !key.Any(c => c is < '!' or > '~');

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization().RequireRateLimiting(RateLimits.Client);
        me.MapGet("/balances", BalancesAsync);
        // Wplata to w prawdziwym brokerze przelew z banku; tu symuluje go klient, zeby mozna bylo zagrac.
        me.MapPost("/deposits", (MoneyRequest body, [FromHeader(Name = IdempotencyHeader)] string? key,
            HttpContext http, ILedgerStore ledger, CancellationToken ct) =>
            PostAsync(http, key, body, JournalEntries.Deposit, ledger, ct));
        me.MapPost("/withdrawals", (MoneyRequest body, [FromHeader(Name = IdempotencyHeader)] string? key,
            HttpContext http, ILedgerStore ledger, CancellationToken ct) =>
            PostAsync(http, key, body, JournalEntries.Withdrawal, ledger, ct));
        me.MapGet("/history", HistoryAsync);
        return app;
    }

    private static async Task<IResult> BalancesAsync(HttpContext http, ILedgerQueries queries, CancellationToken ct)
    {
        var accounts = await queries.ClientBalancesAsync(http.User.ClientId(), ct);
        var balances = accounts
            .GroupBy(a => a.Account.Asset)
            .Select(asset => new
            {
                asset = asset.Key.Code,
                kind = asset.Key.Kind.ToString(),
                available = Amounts.Format(BalanceOf(asset, AccountType.ClientAvailable), asset.Key),
                blocked = Amounts.Format(BalanceOf(asset, AccountType.ClientBlocked), asset.Key),
            });
        return Results.Ok(balances);
    }

    private static async Task<IResult> PostAsync(
        HttpContext http, string? key, MoneyRequest body, Func<ClientId, Money, JournalEntry> operation,
        ILedgerStore ledger, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidKey(key))
        {
            errors[IdempotencyHeader] = [KeyError];
        }
        var money = TryMoney(body, errors);
        if (money is null || errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var client = http.User.ClientId();
        // Klucz z prefiksem klienta: dwa rozne konta moga uzyc tego samego klucza i sobie nie przeszkodza.
        var outcome = await ledger.PostAsync($"{client}:{key}", operation(client, money), ct);
        return outcome == PostOutcome.Posted
            ? Results.Json(new { outcome = "posted" }, statusCode: StatusCodes.Status201Created)
            : Results.Ok(new { outcome = "alreadyPosted" });
    }

    private static async Task<IResult> HistoryAsync(
        HttpContext http, ILedgerQueries queries, CancellationToken ct, int? limit = null, long? before = null)
    {
        var size = limit ?? DefaultHistoryPage;
        if (size is < 1 or > ILedgerQueries.MaxHistoryPage)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = [$"limit must be 1-{ILedgerQueries.MaxHistoryPage}"],
            });
        }
        var entries = await queries.ClientHistoryAsync(http.User.ClientId(), size, before, ct);
        var items = entries.Select(e => new
        {
            id = e.Id,
            sequence = e.Sequence,
            description = e.Description,
            createdAt = e.CreatedAt,
            orderId = e.Order?.Value,
            // Zmiany sald klienta: "+" gdy saldo rosnie, "-" gdy maleje.
            changes = e.Postings.Where(p => p.Account.Client is not null).Select(p => new
            {
                account = p.Account.Type == AccountType.ClientBlocked ? "blocked" : "available",
                asset = p.Account.Asset.Code,
                change = (p.Side == p.Account.NormalSide ? "+" : "-") + Amounts.Format(p.Amount, p.Account.Asset),
            }),
            // Caly dokument po ksiegowemu: strona Wn/Ma i czyje konto (klienta albo brokera).
            postings = e.Postings.Select(p => new
            {
                owner = p.Account.Client is null ? "broker" : "client",
                account = AccountName(p.Account.Type),
                asset = p.Account.Asset.Code,
                side = p.Side == PostingSide.Debit ? "debit" : "credit",
                amount = Amounts.Format(p.Amount, p.Account.Asset),
            }),
        }).ToList();
        // Pelna strona = moze byc dalej; kursor to numer kolejny ostatniego dokumentu.
        return Results.Ok(new { items, nextBefore = items.Count == size ? items[^1].sequence : (long?)null });
    }

    private static string AccountName(AccountType type) => type switch
    {
        AccountType.ClientAvailable => "available",
        AccountType.ClientBlocked => "blocked",
        AccountType.Bank => "bank",
        AccountType.Depository => "depository",
        AccountType.FeeIncome => "feeIncome",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static decimal BalanceOf(IEnumerable<AccountBalance> accounts, AccountType type) =>
        accounts.Where(a => a.Account.Type == type).Sum(a => a.Balance);

    private static Money? TryMoney(MoneyRequest body, Dictionary<string, string[]> errors)
    {
        if (!Amounts.TryParse(body.Amount, out var amount) || amount <= 0m || amount > Posting.MaxAmount)
        {
            errors["amount"] = [$"amount must be a positive decimal string like \"250.40\", at most {Posting.MaxAmount}"];
            return null;
        }
        // Tylko waluty, w ktorych broker prowadzi rachunki — inaczej kazdy kod z trzech liter
        // zakladalby nowe konto banku.
        var currency = SupportedCurrencies.FirstOrDefault(c => c.Code == body.Currency);
        if (currency is null)
        {
            // Stryker disable once String : przy jednej walucie separator nie ma znaczenia
            errors["currency"] = [$"supported currencies: {string.Join(", ", SupportedCurrencies)}"];
            return null;
        }
        try
        {
            return Money.Of(amount, currency);
        }
        catch (ArgumentException e)
        {
            errors["amount"] = [e.Message];
            return null;
        }
    }
}
