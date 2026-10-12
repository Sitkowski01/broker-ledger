using System.Collections.Concurrent;
using System.Net;
using System.Text;
using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Serwer NBP w pamieci: kolejne odpowiedzi z kolejki, potem zawsze ostatnia. Liczy zapytania.</summary>
internal sealed class FakeNbp : HttpMessageHandler
{
    public const string Table = """
        [{"table":"A","no":"198/A/NBP/2026","effectiveDate":"2026-10-09","rates":[
          {"currency":"euro","code":"EUR","mid":4.2512},
          {"currency":"dolar amerykanski","code":"USD","mid":3.6505},
          {"currency":"zepsuty","code":"eu","mid":1.0},
          {"currency":"bez kursu","code":"XXX","mid":0}]}]
        """;

    private readonly ConcurrentQueue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private Func<CancellationToken, Task<HttpResponseMessage>> _last = _ => Task.FromResult(Json(Table));
    private int _calls;

    public int Calls => _calls;
    public Uri? LastUri { get; private set; }

    public FakeNbp Then(params Func<CancellationToken, Task<HttpResponseMessage>>[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
            _last = response;
        }
        return this;
    }

    public static Func<CancellationToken, Task<HttpResponseMessage>> Status(HttpStatusCode code) =>
        _ => Task.FromResult(new HttpResponseMessage(code));

    public static Func<CancellationToken, Task<HttpResponseMessage>> Body(string json) => _ => Task.FromResult(Json(json));

    public static Func<CancellationToken, Task<HttpResponseMessage>> Ok => Body(Table);

    /// <summary>Nie odpowiada, dopoki nie przerwie go limit czasu.</summary>
    public static Func<CancellationToken, Task<HttpResponseMessage>> Hang => async ct =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastUri = request.RequestUri;
        return (_responses.TryDequeue(out var next) ? next : _last)(cancellationToken);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class NbpTableSourceTests
{
    private static Task<ExchangeRateTable> FetchAsync(FakeNbp nbp) =>
        new NbpTableSource(new HttpClient(nbp) { BaseAddress = new Uri("https://nbp.test/") }).LatestAsync(CancellationToken.None);

    [Fact]
    public async Task Reads_table_a_and_skips_entries_it_cannot_use()
    {
        var nbp = new FakeNbp();

        var table = await FetchAsync(nbp);

        Assert.Equal("https://nbp.test/api/exchangerates/tables/A/?format=json", nbp.LastUri!.ToString());
        Assert.Equal(("198/A/NBP/2026", new DateOnly(2026, 10, 9)), (table.Number, table.EffectiveDate));
        Assert.Equal(new Dictionary<Currency, decimal> { [new("EUR")] = 4.2512m, [new("USD")] = 3.6505m }, table.MidRates);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"no":"1/A/NBP/2026","effectiveDate":"2026-01-02"}]""")]
    [InlineData("""[{"no":"1/A/NBP/2026","rates":[]}]""")]
    [InlineData("""[{"effectiveDate":"2026-01-02","rates":[]}]""")]
    [InlineData("""[{"no":"1","effectiveDate":"2026-01-02","rates":[]},{"no":"2","effectiveDate":"2026-01-02","rates":[]}]""")]
    public async Task Response_without_exactly_one_table_is_an_error(string json) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => FetchAsync(new FakeNbp().Then(FakeNbp.Body(json))));

    [Fact]
    public async Task Error_status_is_an_error() =>
        await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(new FakeNbp().Then(FakeNbp.Status(HttpStatusCode.NotFound))));
}
