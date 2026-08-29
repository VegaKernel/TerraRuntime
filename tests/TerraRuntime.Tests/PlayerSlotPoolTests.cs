using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class PlayerSlotPoolTests
{
    [Fact]
    public void Explicit_session_generation_must_be_non_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerSessionGeneration(0));
        Assert.False(default(PlayerSessionGeneration).IsAssigned);
    }

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
        Assert.Equal(PlayerSlotLeaseKind.Connection, first.Kind);
        Assert.Equal(PlayerSlotLeaseKind.Connection, second.Kind);
        Assert.Equal((byte)0, first.Slot.Value);
        Assert.Equal((byte)1, second.Slot.Value);
        Assert.True(first.Generation.IsAssigned);
        Assert.True(second.Generation.IsAssigned);
        Assert.Equal(2, pool.LeasedCount);
        Assert.Equal(2, pool.ConnectionLeasedCount);
        Assert.Equal(0, pool.ServerOwnedLeasedCount);

        PlayerHandle releasedHandle = first.Handle;
        first.Dispose();
        Assert.Equal(1, pool.LeasedCount);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? reused));
        Assert.NotNull(reused);
        Assert.Equal((byte)0, reused.Slot.Value);
        Assert.NotEqual(releasedHandle, reused.Handle);
        Assert.True(reused.Generation.Value > releasedHandle.Generation.Value);

        second.Dispose();
        reused.Dispose();
        Assert.Equal(0, pool.LeasedCount);
    }

    [Fact]
    public void Server_owned_and_connection_players_share_one_exclusive_slot_space()
    {
        var pool = new PlayerSlotPool(2);

        Assert.True(pool.TryAcquireServerOwned(out PlayerSlotPool.PlayerSlotLease? serverOwned));
        Assert.True(pool.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? connection));
        Assert.NotNull(serverOwned);
        Assert.NotNull(connection);
        Assert.Equal(PlayerSlotLeaseKind.ServerOwned, serverOwned.Kind);
        Assert.Equal(PlayerSlotLeaseKind.Connection, connection.Kind);
        Assert.Equal((byte)0, serverOwned.Slot.Value);
        Assert.Equal((byte)1, connection.Slot.Value);
        Assert.Equal(2, pool.LeasedCount);
        Assert.Equal(1, pool.ConnectionLeasedCount);
        Assert.Equal(1, pool.ServerOwnedLeasedCount);

        Assert.False(pool.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? blockedConnection));
        Assert.False(pool.TryAcquireServerOwned(out PlayerSlotPool.PlayerSlotLease? blockedServerOwned));
        Assert.Null(blockedConnection);
        Assert.Null(blockedServerOwned);

        PlayerHandle serverHandle = serverOwned.Handle;
        serverOwned.Dispose();
        Assert.Equal(1, pool.LeasedCount);
        Assert.Equal(1, pool.ConnectionLeasedCount);
        Assert.Equal(0, pool.ServerOwnedLeasedCount);

        Assert.True(pool.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? replacement));
        Assert.NotNull(replacement);
        Assert.Equal(serverHandle.Slot, replacement.Slot);
        Assert.True(replacement.Generation.Value > serverHandle.Generation.Value);
        Assert.NotEqual(serverHandle, replacement.Handle);

        connection.Dispose();
        replacement.Dispose();
    }

    [Fact]
    public void Generations_advance_independently_for_each_reused_slot()
    {
        var pool = new PlayerSlotPool(2);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? first));
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? second));
        Assert.NotNull(first);
        Assert.NotNull(second);

        PlayerHandle firstHandle = first.Handle;
        PlayerHandle secondHandle = second.Handle;
        first.Dispose();

        Assert.True(pool.TryAcquireServerOwned(out PlayerSlotPool.PlayerSlotLease? replacement));
        Assert.NotNull(replacement);
        Assert.Equal(firstHandle.Slot, replacement.Handle.Slot);
        Assert.NotEqual(firstHandle.Generation, replacement.Generation);
        Assert.Equal(PlayerSlotLeaseKind.ServerOwned, replacement.Kind);
        Assert.Equal(secondHandle.Generation, second.Generation);

        second.Dispose();
        replacement.Dispose();
    }

    [Fact]
    public void Lease_release_is_idempotent_and_owner_generation_safe()
    {
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquireServerOwned(out PlayerSlotPool.PlayerSlotLease? lease));
        Assert.NotNull(lease);

        lease.Dispose();
        lease.Dispose();

        Assert.True(lease.IsReleased);
        Assert.Equal(0, pool.LeasedCount);
        Assert.Equal(0, pool.ServerOwnedLeasedCount);
        Assert.True(pool.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? replacement));
        Assert.NotNull(replacement);
        Assert.Equal(PlayerSlotLeaseKind.Connection, replacement.Kind);
        replacement.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Rejects_capacity_outside_protocol_byte_slot_space(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerSlotPool(capacity));
    }
}
