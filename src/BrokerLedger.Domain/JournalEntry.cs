namespace BrokerLedger.Domain;

/// <summary>
/// Dokument ksiegowy: zapisy, ktore w kazdym aktywie maja tyle samo po Wn, co po Ma.
/// Pieniadz nie powstaje ani nie znika — tylko przechodzi z konta na konto.
/// </summary>
public sealed class JournalEntry
{
    public const int MaxDescriptionLength = 200;

    public string Description { get; }
    public IReadOnlyList<Posting> Postings { get; }

    public JournalEntry(string description, IEnumerable<Posting> postings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (description.Length > MaxDescriptionLength)
        {
            throw new ArgumentException($"description must be at most {MaxDescriptionLength} characters", nameof(description));
        }
        ArgumentNullException.ThrowIfNull(postings);
        var list = postings.ToList();
        if (list.Any(p => p is null))
        {
            throw new ArgumentException("postings must not contain null", nameof(postings));
        }

        if (list.Count < 2)
        {
            throw new ArgumentException("an entry needs at least two postings", nameof(postings));
        }
        // To samo konto dwa razy w jednym dokumencie zaciemnia, co sie stalo — zabraniamy.
        var repeated = list.GroupBy(p => p.Account).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
        {
            throw new ArgumentException($"account {repeated.Key} appears more than once", nameof(postings));
        }
        foreach (var asset in list.GroupBy(p => p.Account.Asset))
        {
            var debits = asset.Where(p => p.Side == PostingSide.Debit).Sum(p => p.Amount);
            var credits = asset.Where(p => p.Side == PostingSide.Credit).Sum(p => p.Amount);
            if (debits != credits)
            {
                throw new UnbalancedEntryException(asset.Key, debits, credits);
            }
        }

        Description = description;
        Postings = list.AsReadOnly();
    }

    /// <summary>Ten sam opis i te same zapisy, w dowolnej kolejnosci — po tym poznajemy powtorke.</summary>
    public bool HasSameContentAs(JournalEntry other)
    {
        ArgumentNullException.ThrowIfNull(other);
        // Konto wystepuje w dokumencie najwyzej raz, wiec porownanie zbiorow wystarcza.
        return Description == other.Description && Postings.ToHashSet().SetEquals(other.Postings);
    }

    /// <summary>
    /// Rzuca <see cref="InsufficientBalanceException"/>, gdy po tym dokumencie ktores konto zeszloby ponizej zera.
    /// Gdy brakuje na kilku, zglasza najpierw konto klienta (to jego dotyczy odmowa), potem brokera,
    /// w kolejnosci zapisow. Ta sama regula w pamieci i w bazie — obie ksiegi zglaszaja to samo konto.
    /// </summary>
    public void EnsureNoAccountGoesNegative(Func<AccountId, decimal> balanceOf)
    {
        ArgumentNullException.ThrowIfNull(balanceOf);
        foreach (var posting in Postings.OrderBy(p => p.Account.IsClientAccount ? 0 : 1))
        {
            var balance = balanceOf(posting.Account);
            if (balance + posting.BalanceChange < 0m)
            {
                throw new InsufficientBalanceException(posting.Account, balance, posting.Amount);
            }
        }
    }

    public JournalEntry(string description, params Posting[] postings)
        : this(description, (IEnumerable<Posting>)postings)
    {
    }
}
