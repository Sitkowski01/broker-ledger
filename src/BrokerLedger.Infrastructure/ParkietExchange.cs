using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Klient REST gieldy Parkiet. Zlecenie idzie z clientOrderId = id zlecenia brokera, wiec ponowienie po
/// timeoucie zwraca pierwotny wynik zamiast skladac zlecenie drugi raz.
/// </summary>
internal sealed class ParkietExchange(HttpClient http) : IExchange
{
    public async Task<ExchangeReply> PlaceAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        var body = new
        {
            side = order.Side == OrderSide.Buy ? "BUY" : "SELL",
            type = order.Type == OrderType.Limit ? "LIMIT" : "MARKET",
            // Cena jako tekst, w zlotowkach — tak jak ja przyjmuje Parkiet.
            price = order.LimitPrice?.ToString("0.00", CultureInfo.InvariantCulture),
            quantity = order.Quantity,
            clientOrderId = order.Id.ForExchange,
        };
        try
        {
            using var response = await http.PostAsJsonAsync(
                new Uri($"api/instruments/{order.Symbol}/orders", UriKind.Relative), body, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Created or HttpStatusCode.OK => await AcceptedAsync(response, cancellationToken),
                HttpStatusCode.BadRequest or HttpStatusCode.NotFound =>
                    new ExchangeReply.Refused(await ReasonAsync(response, cancellationToken)),
                HttpStatusCode.ServiceUnavailable =>
                    new ExchangeReply.Busy(await ReasonAsync(response, cancellationToken)),
                // 409: ten clientOrderId ma juz inne zlecenie. Nie powinno sie zdarzyc (id jest unikalne), a zwolnienie
                // srodkow byloby niebezpieczne — zostaje do wyjasnienia, jak brak odpowiedzi.
                _ => new ExchangeReply.NoAnswer($"exchange answered {(int)response.StatusCode}: {await ReasonAsync(response, cancellationToken)}"),
            };
        }
        catch (Exception e) when (IsNoAnswer(e, cancellationToken))
        {
            return new ExchangeReply.NoAnswer(e.Message);
        }
    }

    public async Task<CancelReply> CancelAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.ExchangeOrderId is not { } exchangeOrderId)
        {
            return CancelReply.NotOpen;
        }
        try
        {
            using var response = await http.DeleteAsync(
                new Uri($"api/instruments/{order.Symbol}/orders/{exchangeOrderId}", UriKind.Relative), cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => CancelReply.Cancelled,
                HttpStatusCode.NotFound => CancelReply.NotOpen,
                _ => CancelReply.NoAnswer,
            };
        }
        catch (Exception e) when (IsNoAnswer(e, cancellationToken))
        {
            return CancelReply.NoAnswer;
        }
    }

    /// <summary>
    /// Timeout HttpClienta albo blad sieci — ale nie anulowanie przez wolajacego (np. zamykana aplikacja):
    /// to ma przejsc dalej, zeby nie oznaczac zlecenia jako "nieznane" bez powodu.
    /// </summary>
    private static bool IsNoAnswer(Exception e, CancellationToken cancellationToken) =>
        e is HttpRequestException or JsonException
        || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>
    /// Przyjete tylko z numerem zlecenia. Sukces bez numeru (np. strona bledu proxy z kodem 200) nie jest
    /// przyjeciem — zostaje do wyjasnienia, jak brak odpowiedzi.
    /// </summary>
    private static async Task<ExchangeReply> AcceptedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await JsonAsync(response, cancellationToken);
        return body.ValueKind == JsonValueKind.Object
               && body.TryGetProperty("orderId", out var id) && id.ValueKind == JsonValueKind.Number
               && id.TryGetInt64(out var orderId) && orderId > 0
            ? new ExchangeReply.Accepted(orderId)
            : new ExchangeReply.NoAnswer($"exchange answered {(int)response.StatusCode} without an order number");
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

    private static async Task<string> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await JsonAsync(response, cancellationToken);
            return problem.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text
                ? text
                : response.ReasonPhrase ?? response.StatusCode.ToString();
        }
        catch (JsonException)
        {
            return response.ReasonPhrase ?? response.StatusCode.ToString();
        }
    }
}

