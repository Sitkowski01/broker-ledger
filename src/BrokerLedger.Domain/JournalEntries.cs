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

    /// <summary>
    /// Kupno na gieldzie: z blokady klienta schodzi wartosc i prowizja. Wartosc wychodzi z banku do rozliczenia
    /// transakcji, prowizja zostaje u brokera jako przychod, a akcje wchodza do depozytu na konto klienta.
    /// </summary>
    public static JournalEntry Purchase(ClientId client, string symbol, long quantity, decimal value, decimal fee)
    {
        var postings = new List<Posting>
        {
            Posting.Debit(AccountId.Blocked(client, Asset.Cash(Currency.Pln)), value + fee),
            Posting.Credit(AccountId.Bank(Currency.Pln), value),
            Posting.Debit(AccountId.Depository(symbol), quantity),
            Posting.Credit(AccountId.Available(client, Asset.Shares(symbol)), quantity),
        };
        if (fee > 0m)
        {
            postings.Add(Posting.Credit(AccountId.FeeIncome(Currency.Pln), fee));
        }
        return new JournalEntry("purchase", postings);
    }

    /// <summary>
    /// Sprzedaz na gieldzie: zablokowane akcje wychodza z depozytu, pieniadze wplywaja do banku, klient dostaje
    /// wartosc pomniejszona o prowizje (prowizja nigdy nie przekracza wartosci, wiec nic nie zostaje na minusie).
    /// </summary>
    public static JournalEntry Sale(ClientId client, string symbol, long quantity, decimal value, decimal fee)
    {
        var postings = new List<Posting>
        {
            Posting.Debit(AccountId.Blocked(client, Asset.Shares(symbol)), quantity),
            Posting.Credit(AccountId.Depository(symbol), quantity),
            Posting.Debit(AccountId.Bank(Currency.Pln), value),
        };
        if (value > fee)
        {
            postings.Add(Posting.Credit(AccountId.Available(client, Asset.Cash(Currency.Pln)), value - fee));
        }
        if (fee > 0m)
        {
            postings.Add(Posting.Credit(AccountId.FeeIncome(Currency.Pln), fee));
        }
        return new JournalEntry("sale", postings);
    }

    /// <summary>Akcje przeniesione od innego brokera: trafiaja do depozytu i na konto klienta.</summary>
    public static JournalEntry SharesTransferIn(ClientId client, string symbol, long quantity) =>
        new("shares transfer in",
            Posting.Debit(AccountId.Depository(symbol), quantity),
            Posting.Credit(AccountId.Available(client, Asset.Shares(symbol)), quantity));
}
