using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BrokerLedger.Domain;

/// <summary>
/// Kwota w konkretnej walucie, z dokladnoscia do grosza. Klasa, nie struct:
/// <c>default(Money)</c> nie mialby waluty, a tu kazda kwota musi ja miec.
/// </summary>
public sealed record Money : IComparable<Money>
{
    public decimal Amount { get; }
    public Currency Currency { get; }

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Ulamek grosza to blad wywolujacego, nie cos do cichego zaokraglenia.</summary>
    public static Money Of(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException($"amount must not have fractions of a grosz, got {amount}", nameof(amount));
        }
        return new Money(amount, currency);
    }

    public static Money Pln(decimal amount) => Of(amount, Currency.Pln);

    public static Money Zero(Currency currency) => Of(0m, currency);

    public bool IsZero => Amount == 0m;
    public bool IsNegative => Amount < 0m;

    public static Money operator +(Money left, Money right)
    {
        var currency = SameCurrency(left, right);
        return new Money(left.Amount + right.Amount, currency);
    }

    public static Money operator -(Money left, Money right)
    {
        var currency = SameCurrency(left, right);
        return new Money(left.Amount - right.Amount, currency);
    }

    public static Money operator -(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return new Money(-money.Amount, money.Currency);
    }

    public int CompareTo(Money? other)
    {
        SameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        $"{Amount.ToString("0.00", CultureInfo.InvariantCulture)} {Currency}";

    private static Currency SameCurrency([NotNull] Money? left, [NotNull] Money? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException($"cannot combine {left.Currency} with {right.Currency}");
        }
        return left.Currency;
    }
}
