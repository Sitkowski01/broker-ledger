using System.Net;
using System.Text;
using System.Text.Json;
using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Jak klient gieldy tlumaczy odpowiedzi HTTP Parkietu na decyzje sagi.</summary>
public class ParkietExchangeTests
{
    private static readonly Order Buy = Order.Place(OrderId.New(), ClientId.New(), "k",
        new OrderRequest("CDR", OrderSide.Buy, OrderType.Limit, 250.4m, 10), Commission.Standard);

    [Fact]
    public async Task Sends_the_order_in_parkiets_format()
    {
        var stub = new Stub(HttpStatusCode.Created, """{"orderId":42,"events":[]}""");

        var reply = await Exchange(stub).PlaceAsync(Buy);

        Assert.Equal(new ExchangeReply.Accepted(42), reply);
        Assert.Equal((HttpMethod.Post, "/api/instruments/CDR/orders"), (stub.Method, stub.Path));
        var body = JsonDocument.Parse(stub.Body!).RootElement;
        Assert.Equal(("BUY", "LIMIT", "250.40", 10L, Buy.Id.ForExchange),
            (body.GetProperty("side").GetString(), body.GetProperty("type").GetString(), body.GetProperty("price").GetString(),
             body.GetProperty("quantity").GetInt64(), body.GetProperty("clientOrderId").GetString()));
    }

    [Fact]
    public async Task Market_sell_goes_without_a_price()
    {
        var sell = Order.Place(OrderId.New(), ClientId.New(), "k", new OrderRequest("PKO", OrderSide.Sell, OrderType.Market, null, 3), Commission.Standard);
        var stub = new Stub(HttpStatusCode.OK, """{"orderId":7,"repeated":true,"events":[]}""");

        Assert.Equal(new ExchangeReply.Accepted(7), await Exchange(stub).PlaceAsync(sell));

        var body = JsonDocument.Parse(stub.Body!).RootElement;
        Assert.Equal(("SELL", "MARKET"), (body.GetProperty("side").GetString(), body.GetProperty("type").GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("price").ValueKind);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, typeof(ExchangeReply.Refused))]
    [InlineData(HttpStatusCode.NotFound, typeof(ExchangeReply.Refused))]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(ExchangeReply.Busy))]
    [InlineData(HttpStatusCode.GatewayTimeout, typeof(ExchangeReply.NoAnswer))]
    [InlineData(HttpStatusCode.Conflict, typeof(ExchangeReply.NoAnswer))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(ExchangeReply.NoAnswer))]
    public async Task Maps_status_codes_to_saga_decisions(HttpStatusCode status, Type expected)
    {
        var reply = await Exchange(new Stub(status, """{"detail":"why"}""")).PlaceAsync(Buy);

        Assert.IsType(expected, reply);
    }

    [Fact]
    public async Task Carries_the_exchanges_reason()
    {
        Assert.Equal(new ExchangeReply.Refused("limit order requires a price"),
            await Exchange(new Stub(HttpStatusCode.BadRequest, """{"detail":"limit order requires a price"}""")).PlaceAsync(Buy));
        Assert.Equal(new ExchangeReply.Busy("Service Unavailable"),
            await Exchange(new Stub(HttpStatusCode.ServiceUnavailable, "not json")).PlaceAsync(Buy));
        Assert.Equal(new ExchangeReply.Busy("Service Unavailable"),
            await Exchange(new Stub(HttpStatusCode.ServiceUnavailable, """{"detail":""}""")).PlaceAsync(Buy));
        Assert.Equal(new ExchangeReply.NoAnswer("exchange answered 504: engine busy"),
            await Exchange(new Stub(HttpStatusCode.GatewayTimeout, """{"detail":"engine busy"}""")).PlaceAsync(Buy));
    }

    [Fact]
    public async Task Garbled_success_is_not_an_acceptance()
    {
        var reply = await Exchange(new Stub(HttpStatusCode.Created, "<html>")).PlaceAsync(Buy);

        Assert.IsType<ExchangeReply.NoAnswer>(reply);
    }

    [Fact]
    public async Task Timeout_and_network_errors_are_no_answer()
    {
        Assert.IsType<ExchangeReply.NoAnswer>(await Exchange(new Stub(new TaskCanceledException("timeout"))).PlaceAsync(Buy));
        Assert.IsType<ExchangeReply.NoAnswer>(await Exchange(new Stub(new HttpRequestException("refused"))).PlaceAsync(Buy));
        Assert.Equal(CancelReply.NoAnswer, await Exchange(new Stub(new HttpRequestException("refused"))).CancelAsync(Accepted(1)));
    }

    [Fact]
    public async Task Callers_own_cancellation_is_not_mistaken_for_a_timeout()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Exchange(new Stub(new TaskCanceledException("stopping"))).PlaceAsync(Buy, stopping.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Exchange(new Stub(new TaskCanceledException("stopping"))).CancelAsync(Accepted(1), stopping.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, CancelReply.Cancelled)]
    [InlineData(HttpStatusCode.NotFound, CancelReply.NotOpen)]
    [InlineData(HttpStatusCode.GatewayTimeout, CancelReply.NoAnswer)]
    public async Task Cancel_asks_by_the_exchange_order_number(HttpStatusCode status, CancelReply expected)
    {
        var stub = new Stub(status, "{}");

        Assert.Equal(expected, await Exchange(stub).CancelAsync(Accepted(17)));

        Assert.Equal((HttpMethod.Delete, "/api/instruments/CDR/orders/17"), (stub.Method, stub.Path));
    }

    [Fact]
    public async Task Order_without_an_exchange_number_is_not_open_there()
    {
        var stub = new Stub(HttpStatusCode.OK, "{}");

        Assert.Equal(CancelReply.NotOpen, await Exchange(stub).CancelAsync(Buy));
        Assert.Null(stub.Method);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Exchange(stub).PlaceAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Exchange(stub).CancelAsync(null!));
    }

    private static ParkietExchange Exchange(Stub stub) =>
        new(new HttpClient(stub) { BaseAddress = new Uri("http://parkiet.test/") });

    private static Order Accepted(long exchangeOrderId)
    {
        var order = Order.Place(OrderId.New(), ClientId.New(), "k", Buy.Request, Commission.Standard);
        order.MarkFundsBlocked();
        order.Accept(exchangeOrderId);
        return order;
    }

    private sealed class Stub : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = "";
        private readonly Exception? _failure;

        public Stub(HttpStatusCode status, string body) => (_status, _body) = (status, body);

        public Stub(Exception failure) => _failure = failure;

        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_failure is not null)
            {
                throw _failure;
            }
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData("""{"events":[]}""")]
    [InlineData("""{"orderId":"42"}""")]
    [InlineData("""{"orderId":0}""")]
    [InlineData("""{"orderId":-3}""")]
    [InlineData("""[42]""")]
    public async Task Success_without_an_order_number_is_not_an_acceptance(string body)
    {
        var reply = await Exchange(new Stub(HttpStatusCode.Created, body)).PlaceAsync(Buy);

        Assert.Equal(new ExchangeReply.NoAnswer("exchange answered 201 without an order number"), reply);
    }
}
