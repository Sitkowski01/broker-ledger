using BrokerLedger.Domain;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BrokerLedger.Domain.Tests;

/// <summary>
/// Losowe kupna i sprzedaze jednej spolki po losowych cenach, z prawdziwa prowizja. Jakkolwiek je przeplesc,
/// zaokraglenia sredniej nie moga zgubic ani dorobic grosza: zrealizowany wynik plus koszt tego, co zostalo,
/// to zawsze "dostal minus zaplacil" plus ten koszt.
/// </summary>
public class PortfolioPropertyTests
{
    public sealed record Trade(bool Buy, int Quantity, int PriceGrosze);

    public static class Generators
    {
        public static Arbitrary<Trade[]> Trades() => Arb.From(Gen.ArrayOf(
            from buy in Gen.Elements(true, true, false)
            from quantity in Gen.Choose(1, 50)
            from price in Gen.Choose(1, 100_000)
            select new Trade(buy, quantity, price)));
    }

    [Property(Arbitrary = [typeof(Generators)], MaxTest = 300)]
    public void Average_cost_never_creates_or_loses_a_grosz(Trade[] trades)
    {
        var client = ClientId.New();
        var history = new List<HistoryEntry>();
        long held = 0;
        decimal paid = 0m, received = 0m;
        foreach (var trade in trades)
        {
            // Sprzedac mozna najwyzej to, co sie ma — wiecej nie przepusci ksiega.
            var quantity = trade.Buy ? trade.Quantity : Math.Min(trade.Quantity, held);
            if (quantity == 0)
            {
                continue;
            }
            var value = quantity * (trade.PriceGrosze / 100m);
            var fee = Commission.Standard.ForFilled(Money.Pln(value)).Amount;
            var entry = trade.Buy
                ? JournalEntries.Purchase(client, "CDR", quantity, value, fee)
                : JournalEntries.Sale(client, "CDR", quantity, value, fee);
            history.Add(PortfolioTests.AsSeenBy(client, entry, history.Count + 1));
            held += trade.Buy ? quantity : -quantity;
            paid += trade.Buy ? value + fee : 0m;
            received += trade.Buy ? 0m : value - fee;
        }

        var position = Portfolio.FromHistory(history).Positions.SingleOrDefault() ?? new Position("CDR", 0, 0m, 0m);

        Assert.Equal(held, position.Quantity);
        Assert.Equal(received - paid + position.CostBasis, position.RealizedPnl);
        Assert.InRange(position.CostBasis, 0m, paid);
        Assert.True(position.IsOpen || position.CostBasis == 0m, "an empty position must not keep any cost");
        Assert.Equal(decimal.Round(position.CostBasis, 2), position.CostBasis);
    }
}
