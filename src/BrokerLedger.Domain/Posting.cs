namespace BrokerLedger.Domain;

/// <summary>Jeden zapis: konto, strona i kwota (zawsze dodatnia — kierunek mowi strona).</summary>
public sealed record Posting
{
    public AccountId Account { get; }
    public PostingSide Side { get; }
    public decimal Amount { get; }

    public Posting(AccountId account, PostingSide side, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (amount <= 0m)
        {
            throw new ArgumentException($"amount must be positive, got {amount}", nameof(amount));
        }
        if (!account.Asset.Fits(amount))
        {
            throw new ArgumentException($"{amount} is too precise for {account.Asset}", nameof(amount));
        }
        Account = account;
        Side = side;
        Amount = amount;
    }

    public static Posting Debit(AccountId account, decimal amount) => new(account, PostingSide.Debit, amount);

    public static Posting Credit(AccountId account, decimal amount) => new(account, PostingSide.Credit, amount);

    /// <summary>O ile zapis zmienia saldo konta: plus po stronie normalnej konta, minus po przeciwnej.</summary>
    public decimal BalanceChange => Side == Account.NormalSide ? Amount : -Amount;
}
