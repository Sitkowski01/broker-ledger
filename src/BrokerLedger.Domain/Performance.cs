namespace BrokerLedger.Domain;

/// <summary>
/// Stan rachunku w jednej chwili: <see cref="Value"/> (gotowka + akcje po ostatniej cenie z gieldy)
/// i <see cref="Invested"/> (wplaty minus wyplaty). Wynik to ich roznica — wplata nie jest zarobkiem.
/// </summary>
public sealed record PerformancePoint(DateTimeOffset At, decimal Value, decimal Invested)
{
    public decimal Result => Value - Invested;
}

/// <summary>
/// Wynik rachunku w czasie, odtworzony z dokumentow ksiegi i cen transakcji — bez osobnej tabeli wynikow.
/// Jak <see cref="Portfolio"/>: liczy sie zmiana gotowki i akcji w dokumencie, nie jego opis. Dokument, ktory
/// zmienia gotowke, a nie rusza akcji, to przeplyw z zewnatrz (wplata, wyplata) — zmienia "wplacone", nie wynik.
/// Blokada pod zlecenie przesuwa miedzy kontami klienta, wiec suma sie nie zmienia i sama znika z rachunku.
/// </summary>
public static class Performance
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);

    /// <summary>
    /// Strefa gieldy. Nazwa IANA dziala na Linuksie (tzdata); na Windowsie przy InvariantGlobalization nie ma
    /// mapowania IANA, wiec jest tez nazwa windowsowa.
    /// </summary>
    public static readonly TimeZoneInfo ExchangeZone =
        // Stryker disable once String : testy i CI dzialaja na Linuksie, gdzie strefa IANA jest zawsze; nazwa windowsowa to zapas
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var iana) ? iana
        : TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    /// <param name="entries">Dokumenty klienta (tylko jego zapisy), w dowolnej kolejnosci.</param>
    /// <param name="times">Chwile probek, rosnaco.</param>
    /// <param name="priceAt">Ostatnia cena spolki w chwili z <paramref name="times"/> (indeks), null gdy jeszcze nie bylo transakcji.</param>
    public static IReadOnlyList<PerformancePoint> Sample(
        IEnumerable<HistoryEntry> entries, IReadOnlyList<DateTimeOffset> times, Func<string, int, decimal?> priceAt)
    {
        // Stryker disable once Statement : bez tego OrderBy nizej rzuca ten sam ArgumentNullException
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(priceAt);
        var ordered = entries.OrderBy(e => e.Sequence).ToList();
        var shares = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        decimal cash = 0m, invested = 0m;
        var next = 0;
        var points = new List<PerformancePoint>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            for (; next < ordered.Count && ordered[next].CreatedAt <= times[i]; next++)
            {
                var entry = ordered[next];
                var cashChange = entry.Postings.Where(p => p.Account.Asset == Pln).Sum(p => p.BalanceChange);
                var moved = false;
                foreach (var p in entry.Postings.Where(p => p.Account.Asset.Kind == AssetKind.Shares))
                {
                    shares[p.Account.Asset.Code] = shares.GetValueOrDefault(p.Account.Asset.Code) + p.BalanceChange;
                    moved |= p.BalanceChange != 0;
                }
                cash += cashChange;
                if (!moved)
                {
                    invested += cashChange;
                }
            }
            // Spolka bez zadnej transakcji do tej chwili (np. akcje przeniesione od innego brokera) nie ma ceny — liczy sie jako 0.
            var sharesValue = shares.Where(s => s.Value != 0).Sum(s => s.Value * (priceAt(s.Key, i) ?? 0m));
            points.Add(new PerformancePoint(times[i], cash + sharesValue, invested));
        }
        return points;
    }

    /// <summary>
    /// Probki raz dziennie (koniec dnia w strefie gieldy) od <paramref name="from"/> do <paramref name="now"/>;
    /// ostatnia probka to zawsze "teraz", zeby wykres konczyl sie tam, gdzie wartosc rachunku na Pulpicie.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> DailyTimes(DateTimeOffset from, DateTimeOffset now, TimeZoneInfo zone)
    {
        // Stryker disable once Statement : bez tego TimeZoneInfo.ConvertTime nizej rzuca ten sam ArgumentNullException
        ArgumentNullException.ThrowIfNull(zone);
        var times = new List<DateTimeOffset>();
        var day = TimeZoneInfo.ConvertTime(from, zone).Date;
        while (true)
        {
            var endOfDay = new DateTimeOffset(day.AddDays(1), zone.GetUtcOffset(day.AddDays(1))).AddTicks(-1);
            if (endOfDay >= now)
            {
                break;
            }
            times.Add(endOfDay);
            day = day.AddDays(1);
        }
        times.Add(now);
        return times;
    }
}
