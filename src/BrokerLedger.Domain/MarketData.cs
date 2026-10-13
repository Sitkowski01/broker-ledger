namespace BrokerLedger.Domain;

/// <summary>Cena ostatniej transakcji na gieldzie i kiedy broker sie o niej dowiedzial.</summary>
public sealed record MarketPrice(decimal Price, DateTimeOffset At);

/// <summary>Notowanie do paska kursow: ostatnia cena i poprzednia, z ktorej widac kierunek zmiany.</summary>
public sealed record Quote(string Symbol, decimal Price, decimal? PreviousPrice, DateTimeOffset At)
{
    /// <summary>1 gdy cena wzrosla, -1 gdy spadla, 0 gdy bez zmian albo to pierwsza transakcja.</summary>
    public int Direction => PreviousPrice is { } previous ? Math.Sign(Price - previous) : 0;
}

/// <summary>Ostatnie ceny spolek — z transakcji, ktore broker i tak dostaje z gieldy do rozliczenia.</summary>
public interface IMarketPrices
{
    /// <summary>Tylko spolki, na ktorych byla jakas transakcja; reszty nie ma w slowniku.</summary>
    Task<IReadOnlyDictionary<string, MarketPrice>> LastPricesAsync(
        IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default);

    /// <summary>Notowania wszystkich spolek z transakcjami, alfabetycznie, najwyzej <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Quote>> QuotesAsync(int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tabela kursow srednich NBP (tabela A): ile zlotych za jedna jednostke waluty. NBP publikuje ja raz
/// dziennie w dni robocze, wiec <see cref="EffectiveDate"/> bywa wczorajsza albo piatkowa.
/// </summary>
public sealed record ExchangeRateTable
{
    public ExchangeRateTable(string number, DateOnly effectiveDate, IReadOnlyDictionary<Currency, decimal> midRates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(midRates);
        if (midRates.Values.Any(rate => rate <= 0m))
        {
            throw new ArgumentException("exchange rates must be positive", nameof(midRates));
        }
        Number = number;
        EffectiveDate = effectiveDate;
        MidRates = midRates;
    }

    public string Number { get; }
    public DateOnly EffectiveDate { get; }
    public IReadOnlyDictionary<Currency, decimal> MidRates { get; }

    /// <summary>Kwota w zlotowkach przeliczona na walute po kursie srednim, do grosza (centa).</summary>
    public Money? Convert(Money pln, Currency target)
    {
        ArgumentNullException.ThrowIfNull(pln);
        ArgumentNullException.ThrowIfNull(target);
        if (pln.Currency != Currency.Pln)
        {
            throw new ArgumentException($"only PLN amounts can be converted, got {pln.Currency}", nameof(pln));
        }
        return MidRates.TryGetValue(target, out var rate)
            ? Money.Of(decimal.Round(pln.Amount / rate, 2, MidpointRounding.AwayFromZero), target)
            : null;
    }
}

/// <summary>Tabela kursow i czy jest przeterminowana (NBP nie odpowiadal, wiec to ostatnia znana).</summary>
public sealed record RatesSnapshot(ExchangeRateTable Table, bool Stale);

/// <summary>Kursy walut do pokazania wartosci portfela w innej walucie. Null: zadnej tabeli jeszcze nie bylo.</summary>
public interface IExchangeRates
{
    Task<RatesSnapshot?> CurrentAsync(CancellationToken cancellationToken = default);
}
