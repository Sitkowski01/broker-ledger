using BrokerLedger.Domain;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BrokerLedger.Domain.Tests;

/// <summary>
/// Zlecenie wykonane w losowych kawalkach po losowych cenach (nie wyzej niz limit), czasem anulowane w polowie.
/// Niezaleznie od podzialu: blokada konczy sie na zerze, prowizja jest liczona raz od calej wartosci,
/// a pieniadze i akcje ani nie powstaja, ani nie znikaja.
/// </summary>
public class SettlementPropertyTests
{
    public sealed record Scenario(OrderSide Side, long Quantity, int LimitGrosze, (int Quantity, int PriceGrosze)[] Fills, bool Cancel);

    public static class Generators
    {
        public static Arbitrary<Scenario> Scenario() => Arb.From(
            from side in Gen.Elements(OrderSide.Buy, OrderSide.Sell)
            from quantity in Gen.Choose(1, 40)
            from limit in Gen.Choose(1, 50_000)
            from cuts in Gen.ArrayOf(Gen.Choose(1, 40))
            from prices in Gen.ArrayOf(Gen.Choose(1, limit))
            from cancel in Gen.Elements(false, true)
            // Kupno nie drozej niz limit, sprzedaz nie taniej — inaczej rozliczenie slusznie odmowi.
            select new Scenario(side, quantity, limit,
                Split(quantity, limit, cuts, side == OrderSide.Buy ? prices : [.. prices.Select(p => (2 * limit) - p)]), cancel));

        /// <summary>Kawalki sumuja sie do calosci; przy anulowaniu reszta zostaje dla zdarzenia CANCELLED.</summary>
        private static (int, int)[] Split(int quantity, int limit, int[] cuts, int[] prices)
        {
            var fills = new List<(int, int)>();
            var left = quantity;
            for (var i = 0; i < cuts.Length && left > 0; i++)
            {
                var part = Math.Min(cuts[i], left);
                fills.Add((part, prices.Length == 0 ? limit : prices[i % prices.Length]));
                left -= part;
            }
            if (left > 0)
            {
                fills.Add((left, prices.Length == 0 ? limit : prices[0]));
            }
            return [.. fills];
        }
    }

    [Property(Arbitrary = [typeof(Generators)], MaxTest = 300)]
    public void Any_split_settles_to_the_same_totals(Scenario scenario)
    {
        var client = ClientId.New();
        var ledger = new Ledger();
        ledger.Post(JournalEntries.Deposit(client, Money.Pln(1_000_000m)));
        ledger.Post(JournalEntries.SharesTransferIn(client, "CDR", 100));
        var limit = scenario.LimitGrosze / 100m;
        var order = Order.Place(OrderId.New(), client, "k",
            new OrderRequest("CDR", scenario.Side, OrderType.Limit, limit, scenario.Quantity), Commission.Standard);
        ledger.Post(order.BlockEntry());
        order.MarkFundsBlocked();
        order.Accept(1, 1);

        var fills = scenario.Cancel ? scenario.Fills[..^1] : scenario.Fills;
        var sequence = 0;
        foreach (var (quantity, price) in fills)
        {
            var trade = new ExchangeEvent.Trade("CDR", new EventPosition(1, ++sequence), 2, 1, scenario.Side, price / 100m, quantity);
            ledger.Post(order.TradeEntry(trade, Commission.Standard));
            order.ApplyTrade(trade, Commission.Standard);
        }
        if (scenario.Cancel)
        {
            order.ApplyCancellation(new ExchangeEvent.Cancelled("CDR", new EventPosition(1, ++sequence), 1, order.RemainingQuantity, "REQUESTED"));
        }
        if (order.ReleaseEntry() is { } release)
        {
            ledger.Post(release);
        }

        var value = fills.Sum(f => f.Quantity * (f.PriceGrosze / 100m));
        var fee = Commission.Standard.ForFilled(Money.Pln(value)).Amount;
        var bought = scenario.Side == OrderSide.Buy ? 1 : -1;
        Assert.Equal(scenario.Cancel ? OrderStatus.Cancelled : OrderStatus.Filled, order.Status);
        Assert.Equal((value, fee), (order.Execution.Value, order.Execution.Commission));
        Assert.Equal(0m, ledger.Balance(AccountId.Blocked(client, order.BlockedAsset)));
        Assert.Equal(fee, ledger.Balance(AccountId.FeeIncome(Currency.Pln)));
        Assert.Equal(1_000_000m - (bought * value) - fee, ledger.Balance(AccountId.Available(client, Asset.Cash(Currency.Pln))));
        Assert.Equal(100m + (bought * order.Execution.Quantity), ledger.Balance(AccountId.Available(client, Asset.Shares("CDR"))));
        Assert.Equal(1_000_000m - (bought * value), ledger.Balance(AccountId.Bank(Currency.Pln)));
    }
}
