using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class VanillaServerPlayerStepPhysicsTests
{
    [Fact]
    public void Right_intent_steps_up_full_block_before_tile_collision()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 7, SolidTile());
        using SpawnedServerPlayer player = Spawn(96f, 86f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(
            player.Snapshot,
            ServerPlayerHorizontalIntent.Right,
            out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(96.08f, next.PositionX, 5);
        Assert.Equal(70f, next.PositionY, 5);
        Assert.Equal(0.08f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.True(next.HitFloor);
    }

    [Fact]
    public void Right_intent_steps_down_sixteen_pixels_before_tile_collision()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 9, SolidTile());
        using SpawnedServerPlayer player = Spawn(96f, 86f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(
            player.Snapshot,
            ServerPlayerHorizontalIntent.Right,
            out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(96.08f, next.PositionX, 5);
        Assert.Equal(102f, next.PositionY, 5);
        Assert.Equal(0.08f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.True(next.HitFloor);
    }

    [Fact]
    public void StepUp_is_not_applied_while_rising_faster_than_gravity()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 7, SolidTile());
        using SpawnedServerPlayer player = Spawn(96f, 86f, velocityY: -2f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(
            player.Snapshot,
            ServerPlayerHorizontalIntent.Right,
            out ServerPlayerDryPhysicsStepResult next));

        Assert.NotEqual(70f, next.PositionY);
        Assert.True(next.VelocityY < VanillaServerPlayerDryPhysicsStepper.Gravity);
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(100, 100));

    private static WorldTile SolidTile() =>
        new()
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        };

    private static SpawnedServerPlayer Spawn(
        float positionX,
        float positionY,
        float velocityX = 0f,
        float velocityY = 0f)
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:step-physics");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        Assert.True(states.TrySpawn(id, positionX, positionY, out PlayerStateSnapshot snapshot));
        if (velocityX != 0f || velocityY != 0f)
        {
            Assert.True(states.TrySetMotion(
                lease.Player,
                positionX,
                positionY,
                velocityX,
                velocityY,
                out snapshot));
        }

        return new SpawnedServerPlayer(lease, snapshot);
    }

    private sealed class SpawnedServerPlayer(
        ServerPlayerSlotRegistry.ServerPlayerSlotLease lease,
        PlayerStateSnapshot snapshot) : IDisposable
    {
        public PlayerStateSnapshot Snapshot { get; } = snapshot;

        public void Dispose() => lease.Dispose();
    }
}
