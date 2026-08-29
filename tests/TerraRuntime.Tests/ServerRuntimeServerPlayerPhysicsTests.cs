using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeServerPlayerPhysicsTests
{
    [Fact]
    public void Tick_advances_connection_free_player_through_runtime_owned_dry_physics()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:falling");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        using (lease)
        {
            var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
            Assert.True(states.TrySpawn(id, 96f, 80f, out PlayerStateSnapshot spawned));
            var runtime = new ServerRuntimeState(
                worldTiles: CreateWorld(),
                serverPlayerStates: states);

            runtime.Tick();

            Assert.True(states.TryGet(spawned.Player, out PlayerStateSnapshot moved));
            Assert.Equal(2UL, moved.Revision.Value);
            Assert.Equal(96f, moved.PositionX, 5);
            Assert.Equal(80.4f, moved.PositionY, 5);
            Assert.Equal(0f, moved.VelocityX, 5);
            Assert.Equal(0.4f, moved.VelocityY, 5);
        }
    }

    [Fact]
    public void Tick_does_not_advance_revision_when_floor_collision_leaves_motion_unchanged()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 8, SolidTile());
        tiles.Set(7, 8, SolidTile());

        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:grounded");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        using (lease)
        {
            var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
            Assert.True(states.TrySpawn(id, 96f, 86f, out PlayerStateSnapshot spawned));
            var runtime = new ServerRuntimeState(
                worldTiles: tiles,
                serverPlayerStates: states);

            runtime.Tick();

            Assert.True(states.TryGet(spawned.Player, out PlayerStateSnapshot retained));
            Assert.Equal(1UL, retained.Revision.Value);
            Assert.Equal(86f, retained.PositionY, 5);
            Assert.Equal(0f, retained.VelocityY, 5);
        }
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(100, 100));

    private static WorldTile SolidTile() =>
        new()
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        };
}
