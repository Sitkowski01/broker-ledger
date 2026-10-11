using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class LedgerTests
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private static readonly Asset Cdr = Asset.Shares("CDR");
    private readonly ClientId _client = ClientId.New();
    private readonly Ledger _ledger = new();

    [Fact]
    public void Deposit_shows_up_on_both_sides()
    {
        _ledger.Post(JournalEntries.Deposit(_client, Money.Pln(1000m)));

        Assert.Equal(1000m, _ledger.Balance(AccountId.Bank(Currency.Pln)));
        Assert.Equal(1000m, _ledger.Balance(AccountId.Available(_client, Pln)));
        Assert.Equal(0m, _ledger.Balance(AccountId.Blocked(_client, Pln)));
    }

    [Fact]
    public void Block_and_release_move_money_between_available_and_blocked()
    {
        _ledger.Post(JournalEntries.Deposit(_client, Money.Pln(1000m)));

        _ledger.Post(JournalEntries.Block(_client, Pln, 250.40m));
        Assert.Equal(749.60m, _ledger.Balance(AccountId.Available(_client, Pln)));
        Assert.Equal(250.40m, _ledger.Balance(AccountId.Blocked(_client, Pln)));

        _ledger.Post(JournalEntries.Release(_client, Pln, 250.40m));
        Assert.Equal(1000m, _ledger.Balance(AccountId.Available(_client, Pln)));
        Assert.Equal(0m, _ledger.Balance(AccountId.Blocked(_client, Pln)));
    }

    [Fact]
    public void Withdrawal_takes_money_out_of_the_bank()
    {
        _ledger.Post(JournalEntries.Deposit(_client, Money.Pln(1000m)));
        _ledger.Post(JournalEntries.Withdrawal(_client, Money.Pln(1000m)));

        Assert.Equal(0m, _ledger.Balance(AccountId.Bank(Currency.Pln)));
        Assert.Equal(0m, _ledger.Balance(AccountId.Available(_client, Pln)));
        Assert.Equal(["deposit", "withdrawal"], _ledger.Entries.Select(e => e.Description));
    }

    [Fact]
    public void Cannot_withdraw_more_than_available()
    {
        _ledger.Post(JournalEntries.Deposit(_client, Money.Pln(100m)));
        _ledger.Post(JournalEntries.Block(_client, Pln, 60m));

        var ex = Assert.Throws<InsufficientBalanceException>(() =>
            _ledger.Post(JournalEntries.Withdrawal(_client, Money.Pln(40.01m))));

        Assert.Equal((AccountId.Available(_client, Pln), 40m, 40.01m), (ex.Account, ex.Balance, ex.Requested));
        Assert.Equal($"ClientAvailable:PLN:{_client} has 40, cannot take 40.01", ex.Message);
        Assert.Equal(2, _ledger.Entries.Count);
    }

    [Fact]
    public void Cannot_release_more_than_blocked()
    {
        _ledger.Post(JournalEntries.Deposit(_client, Money.Pln(100m)));
        _ledger.Post(JournalEntries.Block(_client, Pln, 10m));

        Assert.Throws<InsufficientBalanceException>(() =>
            _ledger.Post(JournalEntries.Release(_client, Pln, 10.01m)));
    }

    [Fact]
    public void Can_take_everything_down_to_exactly_zero()
    {
        _ledger.Post(JournalEntries.Deposit(_client, Money.Pln(0.01m)));

        _ledger.Post(JournalEntries.Block(_client, Pln, 0.01m));

        Assert.Equal(0m, _ledger.Balance(AccountId.Available(_client, Pln)));
    }

    [Fact]
    public void Rejected_entry_leaves_no_trace_even_if_its_first_posting_was_fine()
    {
        // Pierwszy zapis (akcje na konto klienta) jest w porzadku, drugi (zabranie gotowki) nie.
        var entry = new JournalEntry("swap",
            Posting.Debit(AccountId.Depository("CDR"), 1m),
            Posting.Credit(AccountId.Available(_client, Cdr), 1m),
            Posting.Debit(AccountId.Available(_client, Pln), 5m),
            Posting.Credit(AccountId.Bank(Currency.Pln), 5m));

        Assert.Throws<InsufficientBalanceException>(() => _ledger.Post(entry));

        Assert.Empty(_ledger.Entries);
        Assert.Equal(0m, _ledger.Balance(AccountId.Available(_client, Cdr)));
    }

    [Fact]
    public void House_accounts_cannot_go_below_zero_either()
    {
        // Broker nie odda gotowki ani akcji, ktorych nie ma — nawet dokumentem bez udzialu klienta.
        Assert.Throws<InsufficientBalanceException>(() => _ledger.Post(new JournalEntry("fee refund",
            Posting.Debit(AccountId.FeeIncome(Currency.Pln), 1m),
            Posting.Credit(AccountId.Bank(Currency.Pln), 1m))));
        Assert.Throws<InsufficientBalanceException>(() => _ledger.Post(new JournalEntry("shares out",
            Posting.Debit(AccountId.Available(_client, Cdr), 1m),
            Posting.Credit(AccountId.Depository("CDR"), 1m))));

        Assert.Empty(_ledger.Entries);
    }

    [Fact]
    public void Shares_transfer_in_lands_in_depository_and_client_account()
    {
        _ledger.Post(JournalEntries.SharesTransferIn(_client, "CDR", 15));

        Assert.Equal(15m, _ledger.Balance(AccountId.Depository("CDR")));
        Assert.Equal(15m, _ledger.Balance(AccountId.Available(_client, Cdr)));
    }

    [Fact]
    public void Clients_do_not_see_each_others_money()
    {
        var other = ClientId.New();
        _ledger.Post(JournalEntries.Deposit(_client, Money.Pln(100m)));

        Assert.Equal(0m, _ledger.Balance(AccountId.Available(other, Pln)));
        Assert.Throws<InsufficientBalanceException>(() =>
            _ledger.Post(JournalEntries.Withdrawal(other, Money.Pln(1m))));
    }

    [Fact]
    public void Rejects_nulls_and_hides_its_list()
    {
        _ledger.Post(JournalEntries.Deposit(_client, Money.Pln(1m)));

        Assert.IsNotType<List<JournalEntry>>(_ledger.Entries);
        Assert.Same(_ledger.Entries, _ledger.Entries);
        Assert.Throws<ArgumentNullException>(() => _ledger.Post(null!));
        Assert.Throws<ArgumentNullException>(() => JournalEntries.Deposit(_client, null!));
        Assert.Throws<ArgumentNullException>(() => JournalEntries.Withdrawal(_client, null!));
    }
}
