namespace BrokerLedger.Domain;

/// <summary>Zapis zszedlby z kontem ponizej zera — np. wyplata wiekszej kwoty niz wolne srodki.</summary>
public sealed class InsufficientBalanceException(AccountId account, decimal balance, decimal requested)
    : Exception($"{account} has {balance}, cannot take {requested}")
{
    public AccountId Account { get; } = account;
    public decimal Balance { get; } = balance;
    public decimal Requested { get; } = requested;
}
