namespace BrokerLedger.Domain;

/// <summary>Kod waluty ISO 4217, np. PLN. Zawsze trzy wielkie litery.</summary>
public sealed record Currency
{
    public static readonly Currency Pln = new("PLN");

    public string Code { get; }

    public Currency(string code)
    {
        if (code is not { Length: 3 } || !code.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException($"currency code must be 3 upper-case letters, got '{code}'", nameof(code));
        }
        Code = code;
    }

    public override string ToString() => Code;
}
