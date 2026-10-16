using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

/// <summary>
/// Ostatnie ceny ze skrzynki zdarzen gieldy. Broker dostaje z Kafki wszystkie transakcje (nie tylko swoich klientow),
/// wiec skrzynka jest tez notowaniem — bez osobnego strumienia kursow. Kolejnosc przyjscia (id) w obrebie spolki
/// to kolejnosc gieldy, bo spolka to jedna partycja Kafki.
/// </summary>
internal sealed class PostgresMarketPrices(IDbContextFactory<LedgerDbContext> contexts) : IMarketPrices
{
    public async Task<IReadOnlyDictionary<string, MarketPrice>> LastPricesAsync(
        IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        // Stryker disable once Block : oszczedza tylko zapytanie — pusta tablica w ANY i tak nic nie zwraca
        if (symbols.Count == 0)
        {
            return new Dictionary<string, MarketPrice>();
        }
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var list = symbols.ToArray();
        // DISTINCT ON bierze pierwszy wiersz kazdej spolki w porzadku ORDER BY — czyli ostatnia transakcje.
        var rows = await db.Inbox.FromSqlInterpolated($"""
                SELECT DISTINCT ON (symbol) * FROM exchange_inbox
                WHERE type = 'TRADE' AND symbol = ANY({list})
                ORDER BY symbol, id DESC
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Symbol, r => new MarketPrice(r.Price!.Value, r.ReceivedAt));
    }

    public async Task<IReadOnlyList<Quote>> QuotesAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // Dla kazdej spolki dwa skoki po indeksie (symbol, id DESC): ostatnia transakcja i ta tuz przed nia.
        var rows = await db.Database.SqlQuery<QuoteRow>($"""
                SELECT s.symbol, t.price, p.price AS previous_price, t.received_at AS at
                FROM (SELECT DISTINCT symbol FROM exchange_inbox WHERE type = 'TRADE') s
                CROSS JOIN LATERAL (
                    SELECT id, price, received_at FROM exchange_inbox
                    WHERE type = 'TRADE' AND symbol = s.symbol ORDER BY id DESC LIMIT 1) t
                LEFT JOIN LATERAL (
                    SELECT price FROM exchange_inbox
                    WHERE type = 'TRADE' AND symbol = s.symbol AND id < t.id ORDER BY id DESC LIMIT 1) p ON true
                ORDER BY s.symbol
                LIMIT {limit}
                """)
            .ToListAsync(cancellationToken);
        return rows.Select(r => new Quote(r.Symbol, r.Price, r.PreviousPrice, r.At)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<decimal?>>> PricesAtAsync(
        IReadOnlyCollection<string> symbols, IReadOnlyList<DateTimeOffset> times, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(times);
        var result = symbols.Distinct().ToDictionary(s => s, s => (IReadOnlyList<decimal?>)new decimal?[times.Count]);
        if (result.Count == 0 || times.Count == 0)
        {
            return result;
        }
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var symbolList = result.Keys.ToArray();
        var timeList = times.Select(t => t.ToUniversalTime()).ToArray();
        // Kazda para (spolka, chwila) to jeden skok po indeksie (symbol, received_at) — bez czytania calej historii.
        var rows = await db.Database.SqlQuery<PriceAtRow>($"""
                SELECT s.symbol, t.ord::int AS position,
                       (SELECT i.price FROM exchange_inbox i
                        WHERE i.type = 'TRADE' AND i.symbol = s.symbol AND i.received_at <= t.at
                        ORDER BY i.received_at DESC LIMIT 1) AS price
                FROM unnest({symbolList}) AS s(symbol)
                CROSS JOIN unnest({timeList}) WITH ORDINALITY AS t(at, ord)
                """)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            ((decimal?[])result[row.Symbol])[row.Position - 1] = row.Price;
        }
        return result;
    }

    private sealed record PriceAtRow(string Symbol, int Position, decimal? Price);

    private sealed record QuoteRow(string Symbol, decimal Price, decimal? PreviousPrice, DateTimeOffset At);
}
