namespace BrokerLedger.Domain;

/// <summary>
/// Adres e-mail klienta, znormalizowany: bez spacji na brzegach i malymi literami,
/// zeby "Jan@X.pl" i "jan@x.pl" nie byly dwoma kontami.
/// </summary>
public sealed record EmailAddress
{
    public const int MaxLength = 254;

    public string Value { get; }

    public EmailAddress(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim().ToLowerInvariant();
        var at = normalized.IndexOf('@', StringComparison.Ordinal);
        var valid = normalized.Length <= MaxLength
            && at > 0
            && at == normalized.LastIndexOf('@')
            && at < normalized.Length - 1
            && !normalized.Any(char.IsWhiteSpace);
        if (!valid)
        {
            throw new ArgumentException($"'{value}' is not a valid e-mail address", nameof(value));
        }
        Value = normalized;
    }

    public override string ToString() => Value;
}
