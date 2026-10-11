using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrokerLedger.Domain;
using Microsoft.Extensions.DependencyInjection;
using static BrokerLedger.Api.Tests.FakeParkiet;

namespace BrokerLedger.Api.Tests;

public sealed partial class OrderEndToEndTests
{
    [Theory]
    [InlineData("""{"symbol":"CDR","side":"buy","type":"market","quantity":1}""", "order")]
    [InlineData("""{"symbol":"CDR","side":"hold","type":"limit","limitPrice":"1","quantity":1}""", "side")]
    [InlineData("""{"symbol":"CDR","side":"0","type":"limit","limitPrice":"1","quantity":1}""", "side")]
    [InlineData("""{"symbol":"CDR","side":"buy","type":"stop","limitPrice":"1","quantity":1}""", "type")]
    [InlineData("""{"symbol":"CDR","side":"buy","type":"limit","limitPrice":"1e3","quantity":1}""", "limitPrice")]
    [InlineData("""{"symbol":"CDR","side":"buy","type":"limit","limitPrice":"1"}""", "quantity")]
    [InlineData("""{"side":"buy","type":"limit","limitPrice":"1","quantity":1}""", "symbol")]
    [InlineData("""{"symbol":"cdr","side":"buy","type":"limit","limitPrice":"1","quantity":1}""", "order")]
    public async Task Invalid_orders_get_400_with_the_field(string json, string field)
    {
        using var http = await FundedClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/orders")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", "bad");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Order_needs_an_idempotency_key()
    {
        using var http = await FundedClientAsync();

        var response = await PlaceAsync(http, null, Buy());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
