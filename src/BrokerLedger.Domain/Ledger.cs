namespace BrokerLedger.Domain;

/// <summary>
/// Ksiega w pamieci. Dokumenty tylko sie dopisuje — nic nie jest zmieniane ani usuwane,
/// a kazde saldo to suma zapisow, wiec zawsze da sie pokazac, skad sie wzielo.
/// Obiekt jednowatkowy: rownolegle ksiegowanie i odrzucanie powtorek zapewni baza danych.
/// </summary>
public sealed class Ledger
{
    private readonly List<JournalEntry> _entries = [];
    // Suma zapisow kazdego konta, aktualizowana tylko w Post — zeby saldo nie wymagalo
    // przechodzenia calej ksiegi. Nigdy nie zmienia sie inaczej niz przez dokument.
    private readonly Dictionary<AccountId, decimal> _balances = [];

    public Ledger() => Entries = _entries.AsReadOnly();

    public IReadOnlyList<JournalEntry> Entries { get; }

    public decimal Balance(AccountId account) => _balances.GetValueOrDefault(account);

    /// <summary>
    /// Ksieguje dokument w calosci albo wcale. Zadne konto nie moze zejsc ponizej zera:
    /// klient nie wyda wiecej, niz ma, a broker nie odda gotowki ani akcji, ktorych nie trzyma.
    /// </summary>
    public void Post(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        foreach (var posting in entry.Postings)
        {
            var balance = Balance(posting.Account);
            if (balance + posting.BalanceChange < 0m)
            {
                throw new InsufficientBalanceException(posting.Account, balance, posting.Amount);
            }
        }

        _entries.Add(entry);
        foreach (var posting in entry.Postings)
        {
            _balances[posting.Account] = Balance(posting.Account) + posting.BalanceChange;
        }
    }
}
