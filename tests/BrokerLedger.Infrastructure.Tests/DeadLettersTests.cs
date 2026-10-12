namespace BrokerLedger.Infrastructure.Tests;

[Collection(SharedPostgres.Name)]
public sealed class DeadLettersTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await postgres.CreateDatabaseAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Keeps_the_message_and_a_shortened_reason()
    {
        var limit = PostgresDeadLetters.MaxErrorLength;
        await _db.DeadLetters().AddAsync(null, new string('x', limit + 5));
        await _db.DeadLetters().AddAsync("[1]", "bad json");
        await _db.DeadLetters().AddAsync("m", new string('y', limit));

        Assert.Equal(1, await _db.CountAsync($"exchange_dead_letters WHERE message IS NULL AND error = repeat('x', {limit})"));
        Assert.Equal(1, await _db.CountAsync("exchange_dead_letters WHERE message = '[1]' AND error = 'bad json'"));
        Assert.Equal(1, await _db.CountAsync($"exchange_dead_letters WHERE message = 'm' AND error = repeat('y', {limit})"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _db.DeadLetters().AddAsync("m", " "));
    }
}
