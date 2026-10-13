using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BrokerLedger.Domain;
using Microsoft.Extensions.Logging;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Tabela A kursow srednich z API NBP (<c>api/exchangerates/tables/A</c>). Ponowienia, limity czasu i bezpiecznik
/// doklada potok odpornosci w HttpClient — ta klasa tylko pyta i sprawdza odpowiedz.
/// </summary>
internal sealed class NbpTableSource(HttpClient http)
{
    public async Task<ExchangeRateTable> LatestAsync(CancellationToken cancellationToken)
    {
        var tables = await http.GetFromJsonAsync<List<NbpTable>>("api/exchangerates/tables/A/?format=json", cancellationToken);
        if (tables is not [{ No: { } number, EffectiveDate: { } date, Rates: { } rates }])
        {
            throw new InvalidDataException("NBP returned no table A");
        }
        var midRates = new Dictionary<Currency, decimal>();
        foreach (var rate in rates)
        {
            // Pojedyncza dziwna pozycja (np. nowy kod) nie przekresla calej tabeli.
            if (rate is { Code: { Length: 3 } code, Mid: > 0m } && code.All(char.IsAsciiLetterUpper))
            {
                midRates[new Currency(code)] = rate.Mid;
            }
        }
        return new ExchangeRateTable(number, DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), midRates);
    }

    private sealed record NbpTable(
        [property: JsonPropertyName("no")] string? No,
        [property: JsonPropertyName("effectiveDate")] string? EffectiveDate,
        [property: JsonPropertyName("rates")] List<NbpRate>? Rates);

    private sealed record NbpRate(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("mid")] decimal Mid);
}

/// <summary>
/// Kursy z pamieci: NBP zmienia tabele raz dziennie, wiec nie pytamy go przy kazdym otwarciu portfela.
/// Gdy NBP nie odpowiada, oddajemy ostatnia znana tabele z flaga "przeterminowana" — portfel ma sie otworzyc,
/// nawet jesli zewnetrzne API lezy. Po nieudanej probie odczekujemy chwile, zeby kazde zadanie
/// nie czekalo na kolejny timeout.
/// </summary>
internal sealed partial class CachedExchangeRates(
    Func<CancellationToken, Task<ExchangeRateTable>> fetch, TimeProvider clock, TimeSpan cacheFor, TimeSpan retryAfter,
    ILogger<CachedExchangeRates> logger) : IExchangeRates, IDisposable
{
    private readonly SemaphoreSlim _refresh = new(1, 1);

    // Caly stan w jednej niezmiennej referencji: czytamy go bez blokady, a zapis referencji jest atomowy
    // (tabela i czas w osobnych polach moglyby zostac odczytane w polowie zapisu).
    private volatile CacheState _state = new(null, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    public async Task<RatesSnapshot?> CurrentAsync(CancellationToken cancellationToken = default)
    {
        // Stryker disable once Block : szybka sciezka bez blokady; to samo sprawdzenie powtarza sie pod blokada
        if (TryCached(_state, out var cached))
        {
            return cached;
        }
        // Jedno zapytanie do NBP naraz: reszta czeka i dostaje jego wynik, zamiast wysylac wlasne.
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            var state = _state;
            if (TryCached(state, out var refreshed))
            {
                return refreshed;
            }
            try
            {
                var table = await fetch(cancellationToken);
                _state = new CacheState(table, clock.GetUtcNow(), DateTimeOffset.MinValue);
                return new RatesSnapshot(table, Stale: false);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                var nextAttempt = clock.GetUtcNow() + retryAfter;
                _state = state with { NextAttempt = nextAttempt };
                // Stryker disable once Statement : samo logowanie, bez wplywu na dzialanie
                LogUnavailable(logger, nextAttempt, e);
                return Snapshot(state, stale: true);
            }
        }
        finally
        {
            _refresh.Release();
        }
    }

    public void Dispose() => _refresh.Dispose();

    /// <summary>
    /// Odpowiedz bez pytania NBP: swieza tabela, a w przerwie po nieudanej probie ostatnia znana
    /// (albo null, jesli zadnej jeszcze nie bylo). False — trzeba zapytac NBP.
    /// </summary>
    private bool TryCached(CacheState state, out RatesSnapshot? snapshot)
    {
        var now = clock.GetUtcNow();
        if (state.Table is not null && now - state.FetchedAt < cacheFor)
        {
            snapshot = Snapshot(state, stale: false);
            return true;
        }
        snapshot = Snapshot(state, stale: true);
        return now < state.NextAttempt;
    }

    private static RatesSnapshot? Snapshot(CacheState state, bool stale) =>
        state.Table is null ? null : new RatesSnapshot(state.Table, stale);

    private sealed record CacheState(ExchangeRateTable? Table, DateTimeOffset FetchedAt, DateTimeOffset NextAttempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "NBP exchange rates unavailable, using the last known table until {NextAttempt}")]
    private static partial void LogUnavailable(ILogger logger, DateTimeOffset nextAttempt, Exception exception);
}
