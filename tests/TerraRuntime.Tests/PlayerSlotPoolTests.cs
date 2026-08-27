using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class PlayerSlotPoolTests
{
    [Fact]
    public void Allocates_lowest_available_slots_and_enforces_capacity()
    {
        var pool = new PlayerSlotPool(2);

        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? first));
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? second));
        Assert.False(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? rejected));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(rejected);
        Assert.Equal((byte)0, first.Slot.Value);
        Assert.Equal((byte)1, second.Slot.Value);
        Assert.Equal(2, pool.LeasedCount);

        first.Dispose();
        Assert.Equal(1, pool.LeasedCount);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? reused));
        Assert.NotNull(reused);
        Assert.Equal((byte)0, reused.Slot.Value);

        second.Dispose();
        reused.Dispose();
        Assert.Equal(0, pool.LeasedCount);
    }

    [Fact]
    public void Lease_release_is_idempotent()
    {
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        Assert.NotNull(lease);

        lease.Dispose();
        lease.Dispose();

        Assert.True(lease.IsReleased);
        Assert.Equal(0, pool.LeasedCount);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? replacement));
        replacement?.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Rejects_capacity_outside_protocol_byte_slot_space(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerSlotPool(capacity));
    }
}
