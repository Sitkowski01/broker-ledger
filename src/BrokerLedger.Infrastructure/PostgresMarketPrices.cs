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
}
