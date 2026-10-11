namespace BrokerLedger.Domain;

/// <summary>
/// Dokument ksiegowy: zapisy, ktore w kazdym aktywie maja tyle samo po Wn, co po Ma.
/// Pieniadz nie powstaje ani nie znika — tylko przechodzi z konta na konto.
/// </summary>
public sealed class JournalEntry
{
    public string Description { get; }
    public IReadOnlyList<Posting> Postings { get; }

    public JournalEntry(string description, IEnumerable<Posting> postings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
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

    public JournalEntry(string description, params Posting[] postings)
        : this(description, (IEnumerable<Posting>)postings)
    {
    }
}
