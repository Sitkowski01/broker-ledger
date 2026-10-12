using System.Net;
using System.Text;

namespace BrokerLedger.Api.Tests;

/// <summary>API kursow NBP w pamieci: tabela A z okraglymi kursami albo — na zyczenie — awaria.</summary>
public sealed class FakeNbp
{
    public const string Table = """
        [{"table":"A","no":"198/A/NBP/2026","effectiveDate":"2026-10-09","rates":[
          {"currency":"euro","code":"EUR","mid":4.25},
          {"currency":"dolar amerykanski","code":"USD","mid":4.0},
          {"currency":"jen (Japonia)","code":"JPY","mid":0.0245}]}]
        """;

    private volatile bool _down;
    private int _calls;

    public int Calls => _calls;

    /// <summary>Od teraz NBP odpowiada 503 (albo znowu dziala).</summary>
    public FakeNbp Down(bool down = true)
    {
        _down = down;
        return this;
    }

    public HttpMessageHandler Handler() => new FakeHandler(this);

    private sealed class FakeHandler(FakeNbp nbp) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref nbp._calls);
            return Task.FromResult(nbp._down
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Table, Encoding.UTF8, "application/json") });
        }
    }
}
