namespace BrokerLedger.Domain;

/// <summary>
/// Akcje jednej spolki w portfelu klienta. <see cref="CostBasis"/> to ile klient zaplacil za akcje, ktore nadal ma
/// (z prowizja), <see cref="RealizedPnl"/> — zysk lub strata ze sprzedanych. Akcje zablokowane pod zlecenie sprzedazy
/// wciaz naleza do klienta, wiec licza sie do <see cref="Quantity"/>.
/// </summary>
public sealed record Position(string Symbol, long Quantity, decimal CostBasis, decimal RealizedPnl)
{
    public bool IsOpen => Quantity > 0;

    /// <summary>Srednia cena zakupu jednej akcji (z prowizja), do grosza. Brak dla pozycji zamknietej.</summary>
    public decimal? AverageCost => IsOpen ? decimal.Round(CostBasis / Quantity, 2, MidpointRounding.AwayFromZero) : null;
}

/// <summary>
/// Portfel klienta odtworzony z ksiegi — nie ma osobnej tabeli pozycji, ktora moglaby sie z ksiega rozjechac.
/// Koszt liczony metoda sredniej wazonej: sprzedaz zdejmuje z kosztu ulamek rowny ulamkowi sprzedanych akcji.
/// </summary>
public sealed class Portfolio
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);

    private Portfolio(IReadOnlyList<Position> positions) => Positions = positions;

    /// <summary>Wszystkie spolki, ktore klient kiedykolwiek mial, alfabetycznie.</summary>
    public IReadOnlyList<Position> Positions { get; }

    public IEnumerable<Position> Open => Positions.Where(p => p.IsOpen);

    public decimal RealizedPnl => Positions.Sum(p => p.RealizedPnl);

    /// <summary>
    /// Sklada portfel z dokumentow klienta (zapisy tylko na jego kontach). Liczy sie zmiana liczby akcji
    /// i gotowki w dokumencie, a nie jego opis: kupno to "akcji przybylo, gotowki ubylo", sprzedaz odwrotnie,
    /// a blokada pod zlecenie przesuwa miedzy kontami klienta i niczego nie zmienia. Akcje przeniesione
    /// od innego brokera wchodza z kosztem 0 — broker nie zna ceny zakupu (jak fiskus przy braku dokumentu).
    /// </summary>
    public static Portfolio FromHistory(IEnumerable<HistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var holdings = new SortedDictionary<string, Holding>(StringComparer.Ordinal);
        foreach (var entry in entries.OrderBy(e => e.Sequence))
        {
            var shares = entry.Postings
                .Where(p => p.Account.Asset.Kind == AssetKind.Shares)
                .GroupBy(p => p.Account.Asset.Code)
                .Select(g => (Symbol: g.Key, Change: (long)g.Sum(p => p.BalanceChange)))
                .Where(s => s.Change != 0)
                .ToList();
            if (shares.Count == 0)
            {
                continue;
            }
            if (shares.Count > 1)
            {
                throw new InvalidOperationException($"entry {entry.Id} moves shares of more than one company");
            }
            var cash = entry.Postings.Where(p => p.Account.Asset == Pln).Sum(p => p.BalanceChange);
            var (symbol, change) = shares[0];
            if (!holdings.TryGetValue(symbol, out var holding))
            {
                holdings[symbol] = holding = new Holding();
            }
            // Stryker disable once Equality : zera odfiltrowane wyzej, wiec ">=" dziala tak samo
            if (change > 0)
            {
                holding.Buy(entry, change, -cash);
            }
            else
            {
                holding.Sell(entry, -change, cash);
            }
        }
        return new Portfolio(holdings.Select(h => h.Value.ToPosition(h.Key)).ToList());
    }

    private sealed class Holding
    {
        private long _quantity;
        private decimal _cost;
        private decimal _realized;

        public void Buy(HistoryEntry entry, long quantity, decimal paid)
        {
            if (paid < 0m)
            {
                throw new InvalidOperationException($"entry {entry.Id} adds shares and pays the client");
            }
            _quantity += quantity;
            _cost += paid;
        }

        public void Sell(HistoryEntry entry, long quantity, decimal received)
        {
            if (received < 0m || quantity > _quantity)
            {
                throw new InvalidOperationException($"entry {entry.Id} is not a valid sale of {quantity} shares");
            }
            // Najpierw mnozenie, potem dzielenie: sprzedaz calej pozycji daje dokladnie caly koszt (decimal liczy
            // dziesietnie), wiec na pustej pozycji nie zostaja grosze z zaokraglen.
            var soldCost = decimal.Round(_cost * quantity / _quantity, 2, MidpointRounding.AwayFromZero);
            _quantity -= quantity;
            _cost -= soldCost;
            _realized += received - soldCost;
        }

        public Position ToPosition(string symbol) => new(symbol, _quantity, _cost, _realized);
    }
}
