using BrokerLedger.Domain;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>Zrodla danych portfela w PostgreSQL: dokumenty z akcjami klienta i ostatnie ceny ze skrzynki gieldy.</summary>
[Collection(SharedPostgres.Name)]
public sealed class PortfolioQueriesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Asset Pln = Asset.Cash(Currency.Pln);
    private readonly ClientId _client = ClientId.New();
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await postgres.CreateDatabaseAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Share_entries_come_oldest_first_with_all_of_the_clients_postings()
    {
        var store = _db.Store();
        await store.PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(1000m)));
        await store.PostAsync("block", JournalEntries.Block(_client, Pln, 503m));
        await store.PostAsync("buy", JournalEntries.Purchase(_client, "CDR", 5, 500m, 3m));
        await store.PostAsync("in", JournalEntries.SharesTransferIn(_client, "PKO", 2));
        await store.PostAsync("other", JournalEntries.SharesTransferIn(ClientId.New(), "CDR", 1));

        var entries = await _db.Queries().ClientShareEntriesAsync(_client);

        Assert.Equal(["purchase", "shares transfer in"], entries.Select(e => e.Description));
        Assert.True(entries[0].Sequence < entries[1].Sequence);
        // Z kupna widac zarowno akcje, jak i zaplacona gotowke — ale nie bank, depozyt ani prowizje brokera.
        Assert.Equal(
            [Posting.Debit(AccountId.Blocked(_client, Pln), 503m), Posting.Credit(AccountId.Available(_client, Asset.Shares("CDR")), 5m)],
            entries[0].Postings);
        Assert.Equal(new Position("CDR", 5, 503m, 0m), Portfolio.FromHistory(entries).Positions[0]);
    }

    [Fact]
    public async Task Client_without_shares_has_no_share_entries()
    {
        await _db.Store().PostAsync("dep", JournalEntries.Deposit(_client, Money.Pln(10m)));

        Assert.Empty(await _db.Queries().ClientShareEntriesAsync(_client));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _db.Queries().ClientShareEntriesAsync(null!));
    }

    private static ExchangeEvent.Trade Trade(string symbol, long sequence, decimal price, long run = 1) =>
        new(symbol, new EventPosition(run, sequence), 4, 9, OrderSide.Buy, price, 1);

    [Fact]
    public async Task Last_price_is_the_latest_trade_of_each_company()
    {
        var inbox = _db.Inbox();
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        await inbox.AddAsync(Trade("CDR", 1, 250m));
        await inbox.AddAsync(Trade("CDR", 2, 251.50m));
        await inbox.AddAsync(Trade("PKO", 1, 48m));
        // Nowy przebieg gieldy zaczyna numeracje od 1 — liczy sie kolejnosc przyjscia, nie numer.
        await inbox.AddAsync(Trade("CDR", 1, 249.10m, run: 2));
        await inbox.AddAsync(new ExchangeEvent.Cancelled("CDR", new EventPosition(2, 2), 9, 1, "REQUESTED"));
        await inbox.AddAsync(Trade("ALE", 1, 30m));

        var prices = await _db.Prices().LastPricesAsync(["CDR", "PKO", "KGH"]);

        Assert.Equal(["CDR", "PKO"], prices.Keys.Order());
        Assert.Equal((249.10m, 48m), (prices["CDR"].Price, prices["PKO"].Price));
        Assert.InRange(prices["CDR"].At, before, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task No_symbols_need_no_query()
    {
        Assert.Empty(await _db.Prices().LastPricesAsync([]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _db.Prices().LastPricesAsync(null!));
    }

    [Fact]
    public async Task Last_trade_lookup_uses_its_index()
    {
        // Bez indeksu kazde otwarcie portfela czytaloby cala historie transakcji gieldy.
        // Pusta tabela i tak czyta sie najtaniej skanem — wylaczamy go, zeby zobaczyc, czy indeks pasuje do zapytania.
        await using var connection = new Npgsql.NpgsqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using (var off = new Npgsql.NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await off.ExecuteNonQueryAsync();
        }
        await using var explain = new Npgsql.NpgsqlCommand(
            "EXPLAIN SELECT DISTINCT ON (symbol) * FROM exchange_inbox WHERE type = 'TRADE' AND symbol = ANY(ARRAY['CDR']) ORDER BY symbol, id DESC",
            connection);
        var plan = string.Join('\n', await ReadAllAsync(explain));

        Assert.Contains("ix_exchange_inbox_last_trade", plan, StringComparison.Ordinal);
    }

    private static async Task<List<string>> ReadAllAsync(Npgsql.NpgsqlCommand command)
    {
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }
        return lines;
    }
}
