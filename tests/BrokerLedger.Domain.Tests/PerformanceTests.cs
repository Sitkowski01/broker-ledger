using BrokerLedger.Domain;
using FsCheck;
using FsCheck.Xunit;

namespace BrokerLedger.Domain.Tests;

public class PerformanceTests
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Warsaw = Performance.ExchangeZone;
    private readonly ClientId _client = ClientId.New();
    private readonly List<HistoryEntry> _history = [];

    /// <summary>Dokument klienta z godzina: <paramref name="hour"/> godzin po <see cref="Start"/>.</summary>
    private void Post(JournalEntry entry, int hour) =>
        _history.Add(new HistoryEntry(Guid.NewGuid(), _history.Count + 1, entry.Description, Start.AddHours(hour),
            entry.Postings.Where(p => p.Account.Client == _client).ToList()));

    private IReadOnlyList<PerformancePoint> Sample(Func<string, int, decimal?> price, params int[] hours) =>
        Performance.Sample(_history, hours.Select(h => Start.AddHours(h)).ToList(), price);

    [Fact]
    public void Deposits_are_not_profit_and_a_purchase_costs_only_the_commission()
    {
        Post(JournalEntries.Deposit(_client, Money.Pln(1000m)), 0);
        Post(JournalEntries.Block(_client, Pln, 1003m), 1);
        Post(JournalEntries.Purchase(_client, "CDR", 10, 1000m, 3m), 2);
        Post(JournalEntries.Release(_client, Pln, 0.01m), 3);

        var points = Sample((_, i) => i < 2 ? 100m : 110m, 0, 2, 4);

        Assert.Equal([(1000m, 1000m, 0m), (997m, 1000m, -3m), (1097m, 1000m, 97m)],
            points.Select(p => (p.Value, p.Invested, p.Result)));
    }

    [Fact]
    public void A_withdrawal_lowers_the_invested_amount_but_not_the_result()
    {
        Post(JournalEntries.Deposit(_client, Money.Pln(500m)), 0);
        Post(JournalEntries.Withdrawal(_client, Money.Pln(200m)), 1);

        var point = Assert.Single(Sample((_, _) => null, 2));

        Assert.Equal((300m, 300m, 0m), (point.Value, point.Invested, point.Result));
    }

    [Fact]
    public void A_sale_turns_shares_into_cash_and_realizes_the_result()
    {
        Post(JournalEntries.Deposit(_client, Money.Pln(1000m)), 0);
        Post(JournalEntries.Purchase(_client, "CDR", 10, 1000m, 0m), 1);
        Post(JournalEntries.Block(_client, Asset.Shares("CDR"), 10), 2);
        Post(JournalEntries.Sale(_client, "CDR", 10, 1200m, 4.68m), 3);

        // Po sprzedazy cena juz nic nie znaczy — akcji nie ma.
        var points = Sample((_, i) => i == 0 ? 120m : 1m, 2, 4);

        Assert.Equal([(1200m, 200m), (1195.32m, 195.32m)], points.Select(p => (p.Value, p.Result)));
    }

    [Fact]
    public void Before_the_first_document_everything_is_zero_and_unpriced_shares_count_as_nothing()
    {
        Post(JournalEntries.SharesTransferIn(_client, "PKN", 5), 5);

        var points = Sample((_, _) => null, 0, 6);

        Assert.Equal([(0m, 0m), (0m, 0m)], points.Select(p => (p.Value, p.Invested)));
    }

    [Property]
    public void Only_deposits_and_withdrawals_never_make_a_result(PositiveInt[] deposits, PositiveInt[] withdrawals)
    {
        var history = new List<HistoryEntry>();
        var client = ClientId.New();
        var all = deposits.Select(d => JournalEntries.Deposit(client, Money.Pln(d.Get)))
            .Concat(withdrawals.Select(w => JournalEntries.Withdrawal(client, Money.Pln(w.Get))))
            .Select((entry, i) => new HistoryEntry(Guid.NewGuid(), i + 1, entry.Description, Start.AddMinutes(i),
                entry.Postings.Where(p => p.Account.Client == client).ToList()));
        history.AddRange(all);

        var points = Performance.Sample(history, [Start.AddMinutes(history.Count / 2), Start.AddDays(1)], (_, _) => 99m);

        Assert.All(points, p => Assert.Equal(0m, p.Result));
    }

    [Fact]
    public void Daily_samples_end_each_Warsaw_day_and_finish_now()
    {
        var times = Performance.DailyTimes(
            new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), Warsaw);

        Assert.Equal([
            new DateTimeOffset(2026, 10, 14, 21, 59, 59, TimeSpan.Zero).AddTicks(9_999_999),
            new DateTimeOffset(2026, 10, 15, 21, 59, 59, TimeSpan.Zero).AddTicks(9_999_999),
            new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
        ], times);
    }

    [Fact]
    public void After_the_clocks_go_back_the_day_ends_an_hour_later_in_UTC()
    {
        // 25.10.2026 Polska wraca na czas zimowy: polnoc 26.10 to 23:00 UTC, a nie 22:00.
        var times = Performance.DailyTimes(
            new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.Zero), Warsaw);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 22, 59, 59, TimeSpan.Zero).AddTicks(9_999_999), times[0]);
        Assert.Equal(2, times.Count);
    }

    [Fact]
    public void Rejects_missing_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => Performance.Sample(null!, [], (_, _) => null));
        Assert.Throws<ArgumentNullException>(() => Performance.Sample([], null!, (_, _) => null));
        Assert.Throws<ArgumentNullException>(() => Performance.Sample([], [], null!));
        Assert.Throws<ArgumentNullException>(() => Performance.DailyTimes(Start, Start, null!));
    }
}
