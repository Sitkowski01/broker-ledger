using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class AccountIdTests
{
    private static readonly ClientId Client = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);

    [Fact]
    public void House_assets_grow_on_debit_and_obligations_on_credit()
    {
        Assert.Equal(PostingSide.Debit, AccountId.Bank(Currency.Pln).NormalSide);
        Assert.Equal(PostingSide.Debit, AccountId.Depository("CDR").NormalSide);
        Assert.Equal(PostingSide.Credit, AccountId.FeeIncome(Currency.Pln).NormalSide);
        Assert.Equal(PostingSide.Credit, AccountId.Available(Client, Pln).NormalSide);
        Assert.Equal(PostingSide.Credit, AccountId.Blocked(Client, Pln).NormalSide);
    }

    [Fact]
    public void Knows_whose_account_it_is()
    {
        Assert.True(AccountId.Available(Client, Pln).IsClientAccount);
        Assert.True(AccountId.Blocked(Client, Pln).IsClientAccount);
        Assert.False(AccountId.Bank(Currency.Pln).IsClientAccount);
        Assert.Null(AccountId.Depository("CDR").Client);
    }

    [Fact]
    public void House_accounts_hold_the_right_kind_of_asset()
    {
        Assert.Equal(Asset.Cash(Currency.Pln), AccountId.Bank(Currency.Pln).Asset);
        Assert.Equal(Asset.Cash(Currency.Pln), AccountId.FeeIncome(Currency.Pln).Asset);
        Assert.Equal(Asset.Shares("CDR"), AccountId.Depository("CDR").Asset);
    }

    [Fact]
    public void Client_accounts_require_a_client_and_an_asset()
    {
        Assert.Equal("client", Assert.Throws<ArgumentNullException>(() => AccountId.Available(null!, Pln)).ParamName);
        Assert.Equal("client", Assert.Throws<ArgumentNullException>(() => AccountId.Blocked(null!, Pln)).ParamName);
        Assert.Throws<ArgumentNullException>(() => AccountId.Available(Client, null!));
    }

    [Fact]
    public void Equal_by_type_asset_and_client()
    {
        Assert.Equal(AccountId.Available(Client, Pln), AccountId.Available(new ClientId(Client.Value), Asset.Cash(Currency.Pln)));
        Assert.NotEqual(AccountId.Available(Client, Pln), AccountId.Blocked(Client, Pln));
        Assert.NotEqual(AccountId.Available(Client, Pln), AccountId.Available(ClientId.New(), Pln));
    }

    [Fact]
    public void Prints_readably()
    {
        Assert.Equal("Bank:PLN", AccountId.Bank(Currency.Pln).ToString());
        Assert.Equal($"ClientBlocked:CDR:{Client}", AccountId.Blocked(Client, Asset.Shares("CDR")).ToString());
    }

    [Fact]
    public void Client_id_must_not_be_empty()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ClientId(Guid.Empty));
        Assert.Equal("value", ex.ParamName);
        Assert.Contains("client id must not be empty", ex.Message, StringComparison.Ordinal);
        Assert.NotEqual(ClientId.New(), ClientId.New());
        Assert.Equal("11111111-1111-1111-1111-111111111111", Client.ToString());
    }
}
