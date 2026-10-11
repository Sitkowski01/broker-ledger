namespace BrokerLedger.Domain;

/// <summary>
/// Konto ksiegi: rodzaj, aktywo i — dla kont klienta — wlasciciel.
/// Konstruktor jest prywatny: fabryki pilnuja, ze bank trzyma gotowke, a depozyt akcje.
/// </summary>
public sealed record AccountId
{
    public AccountType Type { get; }
    public Asset Asset { get; }
    public ClientId? Client { get; }

    private AccountId(AccountType type, Asset asset, ClientId? client)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Type = type;
        Asset = asset;
        Client = client;
    }

    public static AccountId Available(ClientId client, Asset asset) =>
        new(AccountType.ClientAvailable, asset, client ?? throw new ArgumentNullException(nameof(client)));

    public static AccountId Blocked(ClientId client, Asset asset) =>
        new(AccountType.ClientBlocked, asset, client ?? throw new ArgumentNullException(nameof(client)));

    public static AccountId Bank(Currency currency) => new(AccountType.Bank, Asset.Cash(currency), null);

    public static AccountId Depository(string symbol) => new(AccountType.Depository, Asset.Shares(symbol), null);

    public static AccountId FeeIncome(Currency currency) => new(AccountType.FeeIncome, Asset.Cash(currency), null);

    public bool IsClientAccount => Client is not null;

    /// <summary>
    /// Strona, po ktorej konto rosnie. Bank i depozyt to majatek domu maklerskiego (Wn),
    /// konta klienta to zobowiazanie wobec klienta, a prowizja to przychod (Ma).
    /// </summary>
    public PostingSide NormalSide => Type is AccountType.Bank or AccountType.Depository
        ? PostingSide.Debit
        : PostingSide.Credit;

    public override string ToString() =>
        Client is null ? $"{Type}:{Asset}" : $"{Type}:{Asset}:{Client}";
}
