using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure;

/// <summary>Konto domeny rozlozone na kolumny tabeli <c>accounts</c> — i z powrotem.</summary>
internal sealed record AccountKey(AccountType Type, AssetKind AssetKind, string AssetCode, Guid? ClientId, PostingSide NormalSide)
{
    public static AccountKey Of(AccountId account) =>
        new(account.Type, account.Asset.Kind, account.Asset.Code, account.Client?.Value, account.NormalSide);

    /// <summary>Stala kolejnosc zakladania kont — ta sama w kazdej transakcji.</summary>
    public static string SortKey(AccountId account) =>
        $"{account.Type}|{account.Asset.Kind}|{account.Asset.Code}|{account.Client}";

    public static AccountId ToAccountId(AccountRow row)
    {
        var asset = row.AssetKind == AssetKind.Cash ? Asset.Cash(new Currency(row.AssetCode)) : Asset.Shares(row.AssetCode);
        return row.Type switch
        {
            AccountType.ClientAvailable => AccountId.Available(ClientOf(row), asset),
            AccountType.ClientBlocked => AccountId.Blocked(ClientOf(row), asset),
            AccountType.Bank => AccountId.Bank(new Currency(row.AssetCode)),
            AccountType.Depository => AccountId.Depository(row.AssetCode),
            AccountType.FeeIncome => AccountId.FeeIncome(new Currency(row.AssetCode)),
            // Stryker disable once String : nieosiagalne - EF nie wczyta wiersza z nieznanym typem konta
            _ => throw new InvalidOperationException($"unknown account type {row.Type} in row {row.Id}"),
        };
    }

    public IQueryable<AccountRow> Find(IQueryable<AccountRow> accounts) =>
        accounts.Where(a => a.Type == Type && a.AssetKind == AssetKind && a.AssetCode == AssetCode && a.ClientId == ClientId);

    private static ClientId ClientOf(AccountRow row) =>
        new(row.ClientId ?? throw new InvalidOperationException($"client account {row.Id} has no client"));
}
