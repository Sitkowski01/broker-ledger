using BrokerLedger.Api.Accounts;
using BrokerLedger.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace BrokerLedger.Api.Errors;

/// <summary>
/// Wyjatki domeny jako odpowiedzi HTTP (RFC 9457). Tylko te, ktore klient moze zrozumiec i naprawic —
/// reszta zostaje bledem 500, zeby nie ukryc bledu w kodzie pod "zle zadanie".
/// </summary>
internal sealed class LedgerExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, extensions) = exception switch
        {
            // Brak srodkow na koncie brokera to nie wina klienta, tylko blad — zostaje 500.
            InsufficientBalanceException { Account.IsClientAccount: true } e => (
                StatusCodes.Status422UnprocessableEntity,
                "Insufficient funds",
                new Dictionary<string, object?>
                {
                    ["asset"] = e.Account.Asset.Code,
                    ["available"] = Amounts.Format(e.Balance, e.Account.Asset),
                }),
            OrderConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Order was changed at the same time, read it again",
                new Dictionary<string, object?>()),
            IdempotencyConflictException => (
                StatusCodes.Status409Conflict,
                "Idempotency-Key was already used for a different operation",
                new Dictionary<string, object?>()),
            // Stryker disable once String : w tej galezi status i tytul nie sa uzywane
            _ => (0, "", null),
        };
        if (extensions is null)
        {
            return false;
        }

        http.Response.StatusCode = status;
        var written = await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Extensions = extensions },
        });
        if (!written)
        {
            // Klient nie przyjmuje JSON-a (np. Accept: text/plain) — status i tytul i tak musza dojsc.
            await http.Response.WriteAsync(title, cancellationToken);
        }
        return true;
    }
}
