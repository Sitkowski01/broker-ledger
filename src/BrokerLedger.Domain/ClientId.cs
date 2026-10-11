namespace BrokerLedger.Domain;

public sealed record ClientId
{
    public Guid Value { get; }

    public ClientId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("client id must not be empty", nameof(value));
        }
        Value = value;
    }

    public static ClientId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
