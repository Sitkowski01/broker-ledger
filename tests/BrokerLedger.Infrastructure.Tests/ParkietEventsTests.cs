using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Kontrakt z Parkietem: wiadomosci w tej postaci wysyla EventMessage gieldy.</summary>
public class ParkietEventsTests
{
    [Fact]
    public void Reads_a_trade_with_the_price_in_grosze()
    {
        const string json = """
            {"runId":3,"symbol":"CDR","sequence":41,"timestamp":"2026-10-12T10:00:00Z","type":"TRADE",
             "makerOrderId":7,"takerOrderId":9,"takerSide":"SELL","price":25040,"quantity":12}
            """;

        var trade = Assert.IsType<ExchangeEvent.Trade>(ParkietEvents.Parse(json));

        Assert.Equal(new ExchangeEvent.Trade("CDR", new EventPosition(3, 41), 7, 9, OrderSide.Sell, 250.40m, 12), trade);
        Assert.Equal(OrderSide.Buy, Assert.IsType<ExchangeEvent.Trade>(ParkietEvents.Parse(json.Replace("SELL", "BUY", StringComparison.Ordinal))).TakerSide);
    }

    [Fact]
    public void Reads_a_cancellation()
    {
        const string json = """{"runId":1,"symbol":"PKN","sequence":2,"type":"CANCELLED","orderId":5,"quantity":4,"reason":"NO_LIQUIDITY"}""";

        Assert.Equal(new ExchangeEvent.Cancelled("PKN", new EventPosition(1, 2), 5, 4, "NO_LIQUIDITY"), ParkietEvents.Parse(json));
    }

    [Theory]
    [InlineData("RESTED")]
    [InlineData("HALTED")]
    public void Events_that_do_not_move_money_are_skipped(string type) =>
        Assert.Null(ParkietEvents.Parse($$"""{"runId":1,"symbol":"CDR","sequence":1,"type":"{{type}}","orderId":1,"side":"BUY","price":100,"quantity":1}"""));

    [Theory]
    [InlineData("""{"symbol":"CDR","sequence":1,"type":"TRADE"}""", "runId is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","type":"TRADE"}""", "sequence is missing")]
    [InlineData("""{"runId":1,"sequence":1,"type":"TRADE"}""", "symbol is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1}""", "type is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"TRADE","takerOrderId":2,"takerSide":"BUY","price":1,"quantity":1}""", "makerOrderId is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"TRADE","makerOrderId":1,"takerSide":"BUY","price":1,"quantity":1}""", "takerOrderId is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"TRADE","makerOrderId":1,"takerOrderId":2,"takerSide":"BUY","quantity":1}""", "price is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"TRADE","makerOrderId":1,"takerOrderId":2,"takerSide":"BUY","price":1}""", "quantity is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"TRADE","makerOrderId":1,"takerOrderId":2,"takerSide":"buy","price":1,"quantity":1}""", "takerSide must be BUY or SELL, got 'buy'")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"CANCELLED","quantity":1,"reason":"REQUESTED"}""", "orderId is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"CANCELLED","orderId":1,"reason":"REQUESTED"}""", "quantity is missing")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"CANCELLED","orderId":1,"quantity":1}""", "reason is missing")]
    [InlineData("null", "empty message")]
    public void Names_what_is_wrong_with_a_message(string json, string problem) =>
        Assert.Equal(problem, Assert.Throws<FormatException>(() => ParkietEvents.Parse(json)).Message);

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"runId":"one"}""")]
    [InlineData("""{"runId":0,"symbol":"CDR","sequence":1,"type":"TRADE"}""")]
    [InlineData("""{"runId":1,"symbol":"cdr","sequence":1,"type":"CANCELLED","orderId":1,"quantity":1,"reason":"X"}""")]
    [InlineData("""{"runId":1,"symbol":"CDR","sequence":1,"type":"TRADE","makerOrderId":1,"takerOrderId":2,"takerSide":"BUY","price":0,"quantity":1}""")]
    public void Wraps_every_parse_problem_in_one_exception(string? json) =>
        Assert.StartsWith(json is null ? "empty message" : "not an exchange event: ",
            Assert.Throws<FormatException>(() => ParkietEvents.Parse(json)).Message, StringComparison.Ordinal);
}
