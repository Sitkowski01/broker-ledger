namespace BrokerLedger.Domain;

public sealed class UnbalancedEntryException(Asset asset, decimal debits, decimal credits)
    : Exception($"entry does not balance in {asset}: debits {debits}, credits {credits}")
{
    public Asset Asset { get; } = asset;
    public decimal Debits { get; } = debits;
    public decimal Credits { get; } = credits;
}
