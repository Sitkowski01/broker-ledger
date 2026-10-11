using BrokerLedger.Domain;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BrokerLedger.Domain.Tests;

/// <summary>
/// Losowe ciagi operacji (FsCheck) kontra prosty model: slownik "ile klient ma wolnego i zablokowanego".
/// Po kazdym kroku ksiega musi sie bilansowac, zadne konto nie moze miec ujemnego salda,
/// saldo musi byc suma zapisow, a salda klientow musza zgadzac sie z modelem. Przy bledzie FsCheck skraca ciag do najkrotszego przykladu.
/// </summary>
public class LedgerPropertyTests
{
    public enum OpKind
    {
        Deposit,
        Withdraw,
        Block,
        Release,
    }

    public sealed record Op(OpKind Kind, int Client, bool Shares, int Amount);

    public static class Generators
    {
        // Male kwoty i 3 klientow, zeby czesto trafiac na brak srodkow i kwoty "rowno do zera".
        public static Arbitrary<Op> Op() => Arb.From(
            from kind in Gen.Elements(Enum.GetValues<OpKind>())
            from client in Gen.Choose(0, 2)
            from shares in Gen.Elements(false, true)
            from amount in Gen.Choose(1, 500)
            select new Op(kind, client, shares, amount),
            Shrink);

        // Przy bledzie FsCheck najpierw skraca ciag, a potem upraszcza kazda operacje:
        // mniejsza kwota, pierwszy klient, gotowka zamiast akcji.
        private static IEnumerable<Op> Shrink(Op op)
        {
            if (op.Amount > 1)
            {
                yield return op with { Amount = 1 };
                yield return op with { Amount = op.Amount / 2 };
            }
            if (op.Client > 0)
            {
                yield return op with { Client = 0 };
            }
            if (op.Shares)
            {
                yield return op with { Shares = false };
            }
        }
    }

    private static readonly ClientId[] Clients =
        [new(Guid.Parse("00000000-0000-0000-0000-000000000001")),
         new(Guid.Parse("00000000-0000-0000-0000-000000000002")),
         new(Guid.Parse("00000000-0000-0000-0000-000000000003"))];

    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private static readonly Asset Cdr = Asset.Shares("CDR");

    [Property(Arbitrary = [typeof(Generators)], MaxTest = 300)]
    public void Ledger_balances_and_matches_the_model_after_every_step(Op[] ops)
    {
        var ledger = new Ledger();
        var model = new Dictionary<AccountId, decimal>();

        foreach (var op in ops)
        {
            var (entry, expected) = Translate(op, model);
            if (expected is null)
            {
                var entriesBefore = ledger.Entries.Count;
                Assert.Throws<InsufficientBalanceException>(() => ledger.Post(entry));
                Assert.Equal(entriesBefore, ledger.Entries.Count);
            }
            else
            {
                ledger.Post(entry);
                foreach (var (account, balance) in expected)
                {
                    model[account] = balance;
                }
            }

            AssertTrialBalance(ledger);
            foreach (var (account, balance) in model)
            {
                Assert.Equal(balance, ledger.Balance(account));
            }
            foreach (var account in TouchedAccounts(ledger))
            {
                var fromPostings = ledger.Entries.SelectMany(e => e.Postings)
                    .Where(p => p.Account == account).Sum(p => p.BalanceChange);
                Assert.Equal(fromPostings, ledger.Balance(account));
                Assert.True(fromPostings >= 0m, $"{account} went negative");
            }
        }
    }

    /// <summary>
    /// Dokument dla operacji i salda, jakie model przewiduje po nim — albo <c>null</c>,
    /// gdy klientowi zabraknie srodkow i ksiega powinna dokument odrzucic.
    /// </summary>
    private static (JournalEntry Entry, Dictionary<AccountId, decimal>? Expected) Translate(
        Op op, Dictionary<AccountId, decimal> model)
    {
        var client = Clients[op.Client];
        // Wyplacic mozna tylko gotowke; wplata akcji to przeniesienie od innego brokera.
        var shares = op.Shares && op.Kind != OpKind.Withdraw;
        var asset = shares ? Cdr : Pln;
        // Gotowka w groszach: 1..500 groszy, akcje w sztukach.
        var amount = shares ? op.Amount : op.Amount / 100m;
        var available = AccountId.Available(client, asset);
        var blocked = AccountId.Blocked(client, asset);
        decimal Of(AccountId account) => model.GetValueOrDefault(account);

        Dictionary<AccountId, decimal>? Move(AccountId from, AccountId? to) =>
            Of(from) < amount ? null : to is null
                ? new() { [from] = Of(from) - amount }
                : new() { [from] = Of(from) - amount, [to] = Of(to) + amount };

        return op.Kind switch
        {
            OpKind.Deposit when shares =>
                (JournalEntries.SharesTransferIn(client, "CDR", op.Amount), new() { [available] = Of(available) + amount }),
            OpKind.Deposit =>
                (JournalEntries.Deposit(client, Money.Pln(amount)), new() { [available] = Of(available) + amount }),
            OpKind.Withdraw =>
                (JournalEntries.Withdrawal(client, Money.Pln(amount)), Move(available, null)),
            OpKind.Block => (JournalEntries.Block(client, asset, amount), Move(available, blocked)),
            OpKind.Release => (JournalEntries.Release(client, asset, amount), Move(blocked, available)),
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
    }

    private static IEnumerable<AccountId> TouchedAccounts(Ledger ledger) =>
        ledger.Entries.SelectMany(e => e.Postings).Select(p => p.Account).Distinct();

    /// <summary>Bilans probny: w kazdym aktywie suma sald po Wn rowna sie sumie sald po Ma.</summary>
    private static void AssertTrialBalance(Ledger ledger)
    {
        foreach (var asset in TouchedAccounts(ledger).GroupBy(a => a.Asset))
        {
            var debitSide = asset.Where(a => a.NormalSide == PostingSide.Debit).Sum(ledger.Balance);
            var creditSide = asset.Where(a => a.NormalSide == PostingSide.Credit).Sum(ledger.Balance);
            Assert.Equal(debitSide, creditSide);
        }
    }
}
