namespace BrokerLedger.Domain;

public enum AccountType
{
    /// <summary>Wolne srodki klienta — mozna je wyplacic albo zablokowac pod zlecenie.</summary>
    ClientAvailable,

    /// <summary>Srodki klienta zablokowane pod otwarte zlecenie.</summary>
    ClientBlocked,

    /// <summary>Gotowka domu maklerskiego w banku.</summary>
    Bank,

    /// <summary>Akcje, ktore dom maklerski przechowuje w depozycie za klientow.</summary>
    Depository,

    /// <summary>Przychod z prowizji.</summary>
    FeeIncome,
}
