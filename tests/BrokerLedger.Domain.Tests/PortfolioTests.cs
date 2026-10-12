using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class PortfolioTests
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private readonly ClientId _client = ClientId.New();
    private readonly List<HistoryEntry> _history = [];

    /// <summary>Dokument tak, jak zwraca go zapytanie klienta: tylko zapisy na jego kontach.</summary>
    internal static HistoryEntry AsSeenBy(ClientId client, JournalEntry entry, long sequence) =>
        new(Guid.NewGuid(), sequence, entry.Description, DateTimeOffset.UnixEpoch,
            entry.Postings.Where(p => p.Account.Client == client).ToList());

    private void Post(JournalEntry entry) => _history.Add(AsSeenBy(_client, entry, _history.Count + 1));

    private void Buy(string symbol, long quantity, decimal value, decimal fee)
    {
        Post(JournalEntries.Block(_client, Pln, value + fee));
        Post(JournalEntries.Purchase(_client, symbol, quantity, value, fee));
    }

    private void Sell(string symbol, long quantity, decimal value, decimal fee)
    {
        Post(JournalEntries.Block(_client, Asset.Shares(symbol), quantity));
        Post(JournalEntries.Sale(_client, symbol, quantity, value, fee));
    }

    private Portfolio Build() => Portfolio.FromHistory(_history);

    [Fact]
    public void Purchases_add_up_to_an_average_cost_with_commission()
    {
        Post(JournalEntries.Deposit(_client, Money.Pln(10_000m)));
        Buy("CDR", 10, 1000m, 3.90m);
        Buy("CDR", 5, 600m, 3m);

        var position = Assert.Single(Build().Positions);

        Assert.Equal(new Position("CDR", 15, 1606.90m, 0m), position);
        // 1606,90 / 15 = 107,1266... -> 107,13
        Assert.Equal(107.13m, position.AverageCost);
        Assert.True(position.IsOpen);
    }

    [Fact]
    public void Partial_sale_takes_its_share_of_the_cost_and_realizes_the_difference()
    {
        Buy("CDR", 3, 100m, 3m);
        // 1/3 z 103 zl = 34,333... -> 34,33; klient dostaje 50 - 3 = 47 zl.
        Sell("CDR", 1, 50m, 3m);

        var position = Assert.Single(Build().Positions);

        Assert.Equal(new Position("CDR", 2, 68.67m, 12.67m), position);
    }

    [Fact]
    public void Selling_several_shares_takes_their_proportional_cost()
    {
        Buy("CDR", 3, 100m, 3m);
        // 2/3 z 103 zl = 68,666... -> 68,67; klient dostaje 100 - 3 = 97 zl.
        Sell("CDR", 2, 100m, 3m);

        Assert.Equal(new Position("CDR", 1, 34.33m, 28.33m), Assert.Single(Build().Positions));
    }

    [Fact]
    public void Selling_everything_closes_the_position_without_leftover_grosze()
    {
        Buy("PKO", 3, 100m, 3m);
        Sell("PKO", 1, 30m, 3m);
        Sell("PKO", 2, 60m, 3m);

        var portfolio = Build();
        var position = Assert.Single(portfolio.Positions);

        // Zaplacil 103 zl, dostal 27 + 57 = 84 zl.
        Assert.Equal(new Position("PKO", 0, 0m, -19m), position);
        Assert.False(position.IsOpen);
        Assert.Null(position.AverageCost);
        Assert.Empty(portfolio.Open);
        Assert.Equal(-19m, portfolio.RealizedPnl);
    }

    [Fact]
    public void Sale_eaten_by_commission_counts_as_zero_received()
    {
        Buy("ALE", 1, 2m, 2m);
        // Prowizja rowna wartosci: klient nie dostaje nic, w dokumencie nie ma zapisu gotowki klienta.
        Sell("ALE", 1, 2m, 2m);

        Assert.Equal(new Position("ALE", 0, 0m, -4m), Assert.Single(Build().Positions));
    }

    [Fact]
    public void Blocks_releases_deposits_and_withdrawals_do_not_move_the_portfolio()
    {
        Buy("CDR", 4, 400m, 3m);
        Post(JournalEntries.Block(_client, Asset.Shares("CDR"), 4));
        Post(JournalEntries.Release(_client, Asset.Shares("CDR"), 4));
        Post(JournalEntries.Deposit(_client, Money.Pln(50m)));
        Post(JournalEntries.Withdrawal(_client, Money.Pln(50m)));

        Assert.Equal(new Position("CDR", 4, 403m, 0m), Assert.Single(Build().Positions));
    }

    [Fact]
    public void Shares_transferred_in_have_zero_cost()
    {
        Post(JournalEntries.SharesTransferIn(_client, "KGH", 7));
        Sell("KGH", 2, 300m, 3m);

        var position = Assert.Single(Build().Positions);

        Assert.Equal(new Position("KGH", 5, 0m, 297m), position);
        Assert.Equal(0m, position.AverageCost);
    }

    [Fact]
    public void Positions_are_sorted_by_symbol_and_entries_by_sequence()
    {
        Buy("PKO", 1, 50m, 3m);
        Buy("ALE", 2, 60m, 3m);
        Sell("ALE", 2, 80m, 3m);
        _history.Reverse();

        var portfolio = Build();

        Assert.Equal(["ALE", "PKO"], portfolio.Positions.Select(p => p.Symbol));
        Assert.Equal(14m, portfolio.Positions[0].RealizedPnl);
        Assert.Equal(["PKO"], portfolio.Open.Select(p => p.Symbol));
    }

    [Fact]
    public void Empty_history_is_an_empty_portfolio()
    {
        var portfolio = Portfolio.FromHistory([]);

        Assert.Empty(portfolio.Positions);
        Assert.Equal(0m, portfolio.RealizedPnl);
    }
}
