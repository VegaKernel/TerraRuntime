using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerStateStoreTests
{
    [Fact]
    public void Spawn_and_motion_use_exact_server_owned_generation()
    {
        var slots = new PlayerSlotPool(2);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:follower");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);

        Assert.True(states.TrySpawn(id, 100f, 200f, out PlayerStateSnapshot spawned));
        Assert.Equal(lease.Player, spawned.Player);
        Assert.Equal(1UL, spawned.Revision.Value);
        Assert.Equal(100f, spawned.PositionX);
        Assert.Equal(200f, spawned.PositionY);
        Assert.Equal(0f, spawned.VelocityX);
        Assert.Equal(0f, spawned.VelocityY);

        Assert.True(states.TrySetMotion(lease.Player, 101f, 202f, 1f, 2f, out PlayerStateSnapshot moved));
        Assert.Equal(2UL, moved.Revision.Value);
        Assert.Equal(101f, moved.PositionX);
        Assert.Equal(202f, moved.PositionY);
        Assert.Equal(1f, moved.VelocityX);
        Assert.Equal(2f, moved.VelocityY);
        Assert.True(states.TryGet(id, out PlayerStateSnapshot byId));
        Assert.Equal(moved, byId);

        lease.Dispose();
    }

    [Fact]
    public void Connection_owned_handle_cannot_create_or_mutate_server_player_state()
    {
        var slots = new PlayerSlotPool(2);
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? connection));
        Assert.NotNull(connection);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);

        Assert.False(states.TryGet(connection.Handle, out _));
        Assert.False(states.TrySetMotion(connection.Handle, 1f, 2f, 3f, 4f, out _));
        Assert.False(states.TrySetDead(connection.Handle, true, out _));
        Assert.False(states.TryRemove(connection.Handle, out _));

        connection.Dispose();
    }

    [Fact]
    public void Released_identity_immediately_makes_stale_state_unreachable()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:reused");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        Assert.True(states.TrySpawn(id, 10f, 20f, out PlayerStateSnapshot original));
        PlayerHandle stale = original.Player;

        lease.Dispose();

        Assert.False(states.TryGet(id, out _));
        Assert.False(states.TryGet(stale, out _));
        Assert.False(states.TrySetMotion(stale, 30f, 40f, 1f, 1f, out _));

        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var replacement));
        Assert.NotNull(replacement);
        Assert.NotEqual(stale.Generation, replacement.Player.Generation);
        Assert.True(states.TrySpawn(id, 30f, 40f, out PlayerStateSnapshot recreated));
        Assert.Equal(replacement.Player, recreated.Player);
        Assert.Equal(1UL, recreated.Revision.Value);

        replacement.Dispose();
    }

    [Fact]
    public void Invalid_floating_point_state_is_rejected_without_revision_advance()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:finite");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);

        Assert.False(states.TrySpawn(id, float.NaN, 0f, out _));
        Assert.True(states.TrySpawn(id, 0f, 0f, out PlayerStateSnapshot spawned));
        Assert.False(states.TrySetMotion(lease.Player, float.PositiveInfinity, 0f, 0f, 0f, out _));
        Assert.True(states.TryGet(lease.Player, out PlayerStateSnapshot retained));
        Assert.Equal(spawned, retained);
        Assert.Equal(1UL, retained.Revision.Value);

        lease.Dispose();
    }

    [Fact]
    public void Dead_state_is_authoritative_and_generation_safe()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:dead");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        Assert.True(states.TrySpawn(id, 0f, 0f, out _));

        Assert.True(states.TrySetDead(lease.Player, true, out PlayerStateSnapshot dead));
        Assert.True(dead.IsDead);
        Assert.Equal(2UL, dead.Revision.Value);

        Assert.True(states.TryRemove(lease.Player, out PlayerStateSnapshot removed));
        Assert.Equal(dead, removed);
        Assert.False(states.TryGet(lease.Player, out _));

        lease.Dispose();
    }
}
