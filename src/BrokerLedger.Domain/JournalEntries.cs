namespace BrokerLedger.Domain;

/// <summary>Gotowe dokumenty dla operacji brokera. Kazdy to para zapisow Wn / Ma.</summary>
public static class JournalEntries
{
    /// <summary>Klient przelewa pieniadze: gotowka w banku rosnie, rosnie tez dlug wobec klienta.</summary>
    public static JournalEntry Deposit(ClientId client, Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return new JournalEntry("deposit",
            Posting.Debit(AccountId.Bank(amount.Currency), amount.Amount),
            Posting.Credit(AccountId.Available(client, Asset.Cash(amount.Currency)), amount.Amount));
    }

    public static JournalEntry Withdrawal(ClientId client, Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return new JournalEntry("withdrawal",
            Posting.Debit(AccountId.Available(client, Asset.Cash(amount.Currency)), amount.Amount),
            Posting.Credit(AccountId.Bank(amount.Currency), amount.Amount));
    }

    /// <summary>Przeniesienie z wolnych do zablokowanych — pod zlecenie, ktore czeka na gieldzie.</summary>
    public static JournalEntry Block(ClientId client, Asset asset, decimal amount) =>
        new("block",
            Posting.Debit(AccountId.Available(client, asset), amount),
            Posting.Credit(AccountId.Blocked(client, asset), amount));

    public static JournalEntry Release(ClientId client, Asset asset, decimal amount) =>
        new("release",
            Posting.Debit(AccountId.Blocked(client, asset), amount),
            Posting.Credit(AccountId.Available(client, asset), amount));

    /// <summary>Akcje przeniesione od innego brokera: trafiaja do depozytu i na konto klienta.</summary>
    public static JournalEntry SharesTransferIn(ClientId client, string symbol, long quantity) =>
        new("shares transfer in",
            Posting.Debit(AccountId.Depository(symbol), quantity),
            Posting.Credit(AccountId.Available(client, Asset.Shares(symbol)), quantity));
}
