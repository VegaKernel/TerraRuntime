using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerSlotRegistryTests
{
    [Fact]
    public void Stable_server_identity_reserves_shared_wire_slot_against_connections()
    {
        var slots = new PlayerSlotPool(2);
        var registry = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:guide-bot");

        Assert.Equal(
            ServerPlayerSlotAcquireResult.Acquired,
            registry.TryAcquire(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease));
        Assert.NotNull(lease);
        Assert.Equal((byte)0, lease.Player.Slot.Value);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, slots.ServerOwnedLeasedCount);

        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? connection));
        Assert.NotNull(connection);
        Assert.Equal((byte)1, connection.Slot.Value);
        Assert.False(slots.TryAcquireConnection(out _));

        Assert.True(registry.TryGet(id, out ServerPlayerSlotBinding byId));
        Assert.Equal(lease.Player, byId.Player);
        Assert.True(registry.TryGet(lease.Player, out ServerPlayerSlotBinding byHandle));
        Assert.Equal(id, byHandle.Id);

        connection.Dispose();
        lease.Dispose();
    }

    [Fact]
    public void Duplicate_stable_identity_does_not_consume_another_slot()
    {
        var slots = new PlayerSlotPool(2);
        var registry = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:merchant");

        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, registry.TryAcquire(id, out var first));
        Assert.Equal(ServerPlayerSlotAcquireResult.DuplicateId, registry.TryAcquire(id, out var duplicate));
        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, slots.LeasedCount);

        first.Dispose();
    }

    [Fact]
    public void Recreating_identity_after_release_gets_new_generation_and_stale_handle_does_not_resolve()
    {
        var slots = new PlayerSlotPool(1);
        var registry = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:companion");

        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, registry.TryAcquire(id, out var first));
        Assert.NotNull(first);
        PlayerHandle stale = first.Player;
        first.Dispose();

        Assert.False(registry.TryGet(id, out _));
        Assert.False(registry.TryGet(stale, out _));
        Assert.Equal(0, slots.ServerOwnedLeasedCount);

        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, registry.TryAcquire(id, out var replacement));
        Assert.NotNull(replacement);
        Assert.Equal(stale.Slot, replacement.Player.Slot);
        Assert.NotEqual(stale.Generation, replacement.Player.Generation);
        Assert.False(registry.TryGet(stale, out _));
        Assert.True(registry.TryGet(replacement.Player, out _));

        replacement.Dispose();
    }

    [Fact]
    public void Exhausted_shared_pool_reports_no_slot_without_partial_registration()
    {
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? connection));
        var registry = new RuntimeServerPlayerSlotRegistry(slots);

        Assert.Equal(
            ServerPlayerSlotAcquireResult.NoAvailableSlot,
            registry.TryAcquire(new ServerPlayerId("test:blocked"), out var lease));
        Assert.Null(lease);
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, slots.ServerOwnedLeasedCount);

        connection?.Dispose();
    }

    [Fact]
    public void Server_player_id_is_bounded_and_has_no_ambiguous_whitespace()
    {
        Assert.Throws<ArgumentException>(() => new ServerPlayerId(" "));
        Assert.Throws<ArgumentException>(() => new ServerPlayerId("test:bad id"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerPlayerId(new string('x', ServerPlayerId.MaxLength + 1)));
        Assert.True(new ServerPlayerId("test:valid").IsAssigned);
        Assert.False(default(ServerPlayerId).IsAssigned);
    }
}
