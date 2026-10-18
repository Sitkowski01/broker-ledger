using System.Text.Json;
using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Kontrakt topicu <c>exchange.events</c> gieldy Parkiet (EventMessage po stronie Javy): JSON, ceny w groszach
/// jako liczby calkowite. Zdarzenia, ktore nie zmieniaja srodkow (RESTED), i typy nieznane tej wersji
/// brokera zwracamy jako null — gielda moze dodac nowy typ bez psucia konsumenta.
/// </summary>
internal static class ParkietEvents
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Message(
        long? RunId, string? Symbol, long? Sequence, string? Type, long? OrderId, long? MakerOrderId,
        long? TakerOrderId, string? TakerSide, long? Price, long? Quantity, string? Reason, DateTimeOffset? Timestamp);

    /// <exception cref="FormatException">Wiadomosc nie jest poprawnym zdarzeniem gieldy.</exception>
    public static ExchangeEvent? Parse(string? json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<Message>(json ?? "null", Json)
                          ?? throw new FormatException("empty message");
            var position = new EventPosition(Required(message.RunId, "runId"), Required(message.Sequence, "sequence"));
            var symbol = message.Symbol ?? throw new FormatException("symbol is missing");
            ExchangeEvent? parsed = message.Type switch
            {
                "TRADE" => new ExchangeEvent.Trade(symbol, position,
                    Required(message.MakerOrderId, "makerOrderId"), Required(message.TakerOrderId, "takerOrderId"),
                    Side(message.TakerSide), Required(message.Price, "price") / 100m, Required(message.Quantity, "quantity")),
                "CANCELLED" => new ExchangeEvent.Cancelled(symbol, position, Required(message.OrderId, "orderId"),
                    Required(message.Quantity, "quantity"), message.Reason ?? throw new FormatException("reason is missing")),
                null => throw new FormatException("type is missing"),
                _ => null,
            };
            return parsed is null ? null : parsed with { OccurredAt = message.Timestamp };
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new FormatException($"not an exchange event: {e.Message}", e);
        }
    }

    private static long Required(long? value, string name) => value ?? throw new FormatException($"{name} is missing");

    private static OrderSide Side(string? side) => side switch
    {
        "BUY" => OrderSide.Buy,
        "SELL" => OrderSide.Sell,
        _ => throw new FormatException($"takerSide must be BUY or SELL, got '{side}'"),
    };
}
