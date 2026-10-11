namespace BrokerLedger.Domain;

public enum AssetKind
{
    Cash,
    Shares,
}

/// <summary>
/// To, co liczy konto: gotowka w walucie albo akcje spolki. Ksiega bilansuje sie
/// osobno w kazdym aktywie — zlotowek nie da sie zbilansowac akcjami.
/// </summary>
public sealed record Asset
{
    public AssetKind Kind { get; }
    public string Code { get; }

    private Asset(AssetKind kind, string code)
    {
        Kind = kind;
        Code = code;
    }

    public static Asset Cash(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Asset(AssetKind.Cash, currency.Code);
    }

    /// <summary>Symbol spolki jak na gieldzie: 1-12 wielkich liter lub cyfr, np. CDR.</summary>
    public static Asset Shares(string symbol)
    {
        if (symbol is not { Length: >= 1 and <= 12 } || !symbol.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)))
        {
            throw new ArgumentException($"symbol must be 1-12 upper-case letters or digits, got '{symbol}'", nameof(symbol));
        }
        return new Asset(AssetKind.Shares, symbol);
    }

    /// <summary>Gotowka do grosza, akcje w sztukach.</summary>
    public int Scale => Kind == AssetKind.Cash ? 2 : 0;

    public bool Fits(decimal amount) => decimal.Round(amount, Scale) == amount;

    public override string ToString() => Code;
}
