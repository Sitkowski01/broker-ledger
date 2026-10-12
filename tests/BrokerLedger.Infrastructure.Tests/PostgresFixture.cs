using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BrokerLedger.Infrastructure.Tests;

/// <summary>
/// Jeden kontener PostgreSQL na wszystkie testy. Migracje ida raz, do bazy-szablonu;
/// kazdy test dostaje wlasna kopie (CREATE DATABASE ... TEMPLATE), wiec testy sobie nie przeszkadzaja.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Template = "ledger_template";
    // Kazdy test ma wlasna baze i wlasna pule polaczen, a testy wspolbieznosci otwieraja ich po kilkanascie.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithCommand("-c", "max_connections=300")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync($"CREATE DATABASE {Template}");
        await using (var db = new LedgerDbContext(Options(ConnectionString(Template))))
        {
            await db.Database.MigrateAsync();
        }
        // Szablon nie moze miec otwartych polaczen, gdy z niego kopiujemy.
        NpgsqlConnection.ClearAllPools();
    }

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE {name} TEMPLATE {Template}");
        return new TestDatabase(ConnectionString(name));
    }

    /// <summary>Pusta baza bez migracji — do testow samych migracji.</summary>
    public async Task<TestDatabase> CreateEmptyDatabaseAsync()
    {
        var name = $"empty_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE {name}");
        return new TestDatabase(ConnectionString(name));
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    internal static DbContextOptions<LedgerDbContext> Options(string connectionString) =>
        LedgerDb.Configure(new DbContextOptionsBuilder<LedgerDbContext>(), connectionString).Options;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SharedPostgres : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>Swieza baza jednego testu: ksiega, kontrola i surowy SQL do psucia danych.</summary>
public sealed class TestDatabase(string connectionString) : IAsyncDisposable
{
    public string ConnectionString => connectionString;

    private readonly PooledDbContextFactory<LedgerDbContext> _contexts = new(PostgresFixture.Options(connectionString));

    internal PostgresLedgerStore Store(TimeProvider? clock = null) => new(_contexts, clock ?? TimeProvider.System);

    internal LedgerAudit Audit => new(_contexts);

    internal PostgresClientRepository Clients() => new(_contexts, TimeProvider.System);

    internal PostgresLedgerQueries Queries() => new(_contexts);

    internal PostgresOrderRepository Orders(TimeProvider? clock = null) => new(_contexts, clock ?? TimeProvider.System);

    internal PostgresExchangeInbox Inbox() => new(_contexts, TimeProvider.System);

    internal PostgresDeadLetters DeadLetters() => new(_contexts, TimeProvider.System);

    internal PostgresMarketPrices Prices() => new(_contexts);

    internal LedgerDbContext Context() => _contexts.CreateDbContext();

    public async Task<int> ExecuteAsync(string sql)
    {
        await using var db = Context();
        return await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>Zamyka polaczenia tej bazy, zeby nie zjadaly limitu kontenera.</summary>
    public ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return ValueTask.CompletedTask;
    }

    public async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
