using BrokerLedger.Domain;

namespace BrokerLedger.Domain.Tests;

public class OrderIdTests
{
    [Fact]
    public void New_id_carries_the_moment_it_was_made()
    {
        var before = DateTimeOffset.UtcNow.AddMilliseconds(-1);
        var id = OrderId.New();
        var after = DateTimeOffset.UtcNow.AddMilliseconds(1);

        Assert.InRange(id.CreatedAt!.Value, before, after);
    }

    [Fact]
    public void Time_is_read_from_the_first_48_bits_of_a_version_7_uuid()
    {
        // 0x019a0b5c7e40 ms od 1970 = 2025-10-22 10:00:02.368 UTC, potem wersja 7 i losowe bity.
        var id = new OrderId(Guid.Parse("019a0b5c-7e40-7abc-8def-0123456789ab"));

        Assert.Equal(new DateTimeOffset(2025, 10, 22, 10, 0, 2, 368, TimeSpan.Zero), id.CreatedAt);
    }

    [Fact]
    public void Older_ids_without_a_timestamp_have_no_creation_time()
    {
        Assert.Null(new OrderId(Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301")).CreatedAt);
    }
}
