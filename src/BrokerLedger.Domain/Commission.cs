namespace BrokerLedger.Domain;

/// <summary>
/// Prowizja od transakcji: procent od wartosci, ale nie mniej niz minimum. Zaokraglana w gore do grosza —
/// broker nie doklada do prowizji ulamkow grosza.
/// </summary>
public sealed record Commission
{
    /// <summary>Typowa stawka polskiego brokera: 0,39%, min. 3 zl.</summary>
    public static readonly Commission Standard = new(0.0039m, Money.Pln(3m));

    public decimal Rate { get; }
    public Money Minimum { get; }

    public Commission(decimal rate, Money minimum)
    {
        ArgumentNullException.ThrowIfNull(minimum);
        if (rate is < 0m or >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "rate must be in [0, 1)");
        }
        if (minimum.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), minimum, "minimum must not be negative");
        }
        Rate = rate;
        Minimum = minimum;
    }

    public Money For(Money tradeValue)
    {
        ArgumentNullException.ThrowIfNull(tradeValue);
        var fee = Math.Ceiling(tradeValue.Amount * Rate * 100m) / 100m;
        var commission = Money.Of(fee, tradeValue.Currency);
        // Stryker disable once Equality : przy rownych kwotach obie galezie daja to samo
        return commission < Minimum ? Minimum : commission;
    }

    /// <summary>
    /// Prowizja nalezna od calego dotychczasowego wykonania zlecenia. Zlecenie wykonane w czesciach placi
    /// raz od sumy — minimum nie nalicza sie od kazdej transakcji, wiec prowizja nie przekroczy blokady.
    /// Nigdy wiecej niz sama wartosc: sprzedaz jednej akcji za 2 zl nie zadluza klienta.
    /// </summary>
    public Money ForFilled(Money filledValue)
    {
        ArgumentNullException.ThrowIfNull(filledValue);
        if (filledValue.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(filledValue), filledValue, "filled value must not be negative");
        }
        var fee = For(filledValue);
        // Stryker disable once Equality : przy rownych kwotach obie galezie daja to samo
        return fee < filledValue ? fee : filledValue;
    }
}
