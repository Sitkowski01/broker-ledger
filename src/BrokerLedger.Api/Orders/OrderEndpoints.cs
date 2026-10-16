using BrokerLedger.Api.Accounts;
using BrokerLedger.Api.Auth;
using BrokerLedger.Domain;
using Microsoft.AspNetCore.Mvc;

namespace BrokerLedger.Api.Orders;

internal sealed record PlaceOrderRequest(string? Symbol, string? Side, string? Type, string? LimitPrice, long? Quantity);

internal static class OrderEndpoints
{
    public const int DefaultPage = 20;
    public const int MaxPage = 100;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/me/orders").RequireAuthorization().RequireRateLimiting(RateLimits.Client);
        // Stryker disable String : "/" i "" to ta sama sciezka grupy
        orders.MapPost("/", PlaceAsync);
        orders.MapGet("/", ListAsync);
        // Stryker restore String
        orders.MapGet("/{id:guid}", GetAsync);
        orders.MapDelete("/{id:guid}", CancelAsync);
        return app;
    }

    private static async Task<IResult> PlaceAsync(
        PlaceOrderRequest body, [FromHeader(Name = AccountEndpoints.IdempotencyHeader)] string? key,
        HttpContext http, OrderSaga saga, IOrderRepository orders, Commission commission, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!AccountEndpoints.IsValidKey(key))
        {
            errors[AccountEndpoints.IdempotencyHeader] = [AccountEndpoints.KeyError];
        }
        var request = TryRequest(body, errors);
        if (request is null)
        {
            return Results.ValidationProblem(errors);
        }

        Order candidate;
        try
        {
            candidate = Order.Place(OrderId.New(), http.User.ClientId(), key!, request, commission);
        }
        catch (ArgumentException e)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["order"] = [e.Message] });
        }

        var stored = await orders.AddOrGetAsync(candidate, ct);
        if (stored.Id != candidate.Id)
        {
            // Powtorka zadania: pokazujemy zlecenie, jakie jest — dalej poprowadzi je pierwsze zadanie albo reconciler.
            return stored.Request == candidate.Request
                ? Results.Ok(OrderView.Of(stored))
                : Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Idempotency-Key was already used for a different order");
        }

        Order order;
        try
        {
            order = await saga.ProgressAsync(stored, ct);
        }
        catch (OrderConcurrencyException)
        {
            // Reconciler zdazyl sie nim zajac — pokazujemy stan, w jakim jest teraz.
            order = (await orders.GetAsync(stored.Id, ct))!;
        }
        return order.Status switch
        {
            OrderStatus.Rejected => Results.Json(OrderView.Of(order), statusCode: StatusCodes.Status422UnprocessableEntity),
            // Gielda nie odpowiedziala na czas: zlecenie zyje, a jego los wyjasni sie w tle.
            OrderStatus.Unknown => Results.Json(OrderView.Of(order), statusCode: StatusCodes.Status202Accepted),
            _ => Results.Json(OrderView.Of(order), statusCode: StatusCodes.Status201Created),
        };
    }

    private static async Task<IResult> ListAsync(HttpContext http, IOrderRepository orders, CancellationToken ct, int? limit = null)
    {
        var size = limit ?? DefaultPage;
        if (size is < 1 or > MaxPage)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["limit"] = [$"limit must be 1-{MaxPage}"] });
        }
        var list = await orders.ClientOrdersAsync(http.User.ClientId(), size, ct);
        return Results.Ok(list.Select(OrderView.Of));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, IOrderRepository orders, CancellationToken ct)
    {
        var order = await OwnOrderAsync(id, http, orders, ct);
        return order is null ? NotFound() : Results.Ok(OrderView.Of(order));
    }

    private static async Task<IResult> CancelAsync(
        Guid id, HttpContext http, IOrderRepository orders, OrderSaga saga, CancellationToken ct)
    {
        var order = await OwnOrderAsync(id, http, orders, ct);
        if (order is null)
        {
            return NotFound();
        }
        return await saga.CancelAsync(order, ct) switch
        {
            // Srodki zwolni zdarzenie anulowania z gieldy — dopiero ono mowi, ile zdazylo sie zrealizowac.
            // Stan z bazy: rozliczenie moglo juz w tym czasie zamknac zlecenie.
            CancelReply.Cancelled => Results.Accepted(value: OrderView.Of((await orders.GetAsync(order.Id, ct))!)),
            CancelReply.NotOpen => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Order is not open"),
            _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Exchange did not answer, try again"),
        };
    }

    /// <summary>Cudze zlecenie wyglada tak samo jak nieistniejace — nie zdradzamy, ze takie id istnieje.</summary>
    private static async Task<Order?> OwnOrderAsync(Guid id, HttpContext http, IOrderRepository orders, CancellationToken ct)
    {
        var order = id == Guid.Empty ? null : await orders.GetAsync(new OrderId(id), ct);
        return order is not null && order.Client == http.User.ClientId() ? order : null;
    }

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found");

    /// <summary>Zlecenie z tresci albo null, gdy w <paramref name="errors"/> jest jakikolwiek blad — takze dodany wczesniej.</summary>
    private static OrderRequest? TryRequest(PlaceOrderRequest body, Dictionary<string, string[]> errors)
    {
        if (body.Symbol is not { Length: > 0 })
        {
            errors["symbol"] = ["symbol is required"];
        }
        OrderSide? side = body.Side switch { "buy" => OrderSide.Buy, "sell" => OrderSide.Sell, _ => null };
        if (side is null)
        {
            errors["side"] = ["side must be buy or sell"];
        }
        OrderType? type = body.Type switch { "limit" => OrderType.Limit, "market" => OrderType.Market, _ => null };
        if (type is null)
        {
            errors["type"] = ["type must be limit or market"];
        }
        decimal? price = null;
        if (body.LimitPrice is not null)
        {
            if (Amounts.TryParse(body.LimitPrice, out var parsed))
            {
                price = parsed;
            }
            else
            {
                errors["limitPrice"] = ["limitPrice must be a decimal string like \"250.40\""];
            }
        }
        if (body.Quantity is not { } quantity)
        {
            errors["quantity"] = ["quantity is required"];
            return null;
        }
        return errors.Count > 0 ? null : new OrderRequest(body.Symbol!, side!.Value, type!.Value, price, quantity);
    }
}

internal sealed record OrderView(
    Guid Id, string Symbol, string Side, string Type, string? LimitPrice, long Quantity, string Status,
    long? ExchangeOrderId, string? RejectionReason, string BlockedAsset, string Blocked,
    long FilledQuantity, string FilledValue, string Commission, DateTimeOffset? CreatedAt,
    bool FundsBlocked, bool FundsReleased)
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);

    public static OrderView Of(Order order) => new(
        order.Id.Value, order.Symbol, order.Side.ToString().ToLowerInvariant(), order.Type.ToString().ToLowerInvariant(),
        order.LimitPrice is { } price ? Amounts.Format(price, Pln) : null,
        order.Quantity, order.Status.ToString().ToLowerInvariant(), order.ExchangeOrderId, order.RejectionReason,
        order.BlockedAsset.Code, Amounts.Format(order.BlockedAmount, order.BlockedAsset),
        order.Execution.Quantity, Amounts.Format(order.Execution.Value, Pln), Amounts.Format(order.Execution.Commission, Pln),
        order.Id.CreatedAt, order.FundsBlocked, order.FundsReleased);
}
