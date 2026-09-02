using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class VanillaServerPlayerHorizontalPhysicsTests
{
    [Fact]
    public void Right_intent_accelerates_before_gravity_and_collision()
    {
        WorldTileStore tiles = CreateWorld();
        using SpawnedServerPlayer player = Spawn(96f, 80f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(
            player.Snapshot,
            ServerPlayerHorizontalIntent.Right,
            out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(96.08f, next.PositionX, 5);
        Assert.Equal(80.4f, next.PositionY, 5);
        Assert.Equal(0.08f, next.VelocityX, 5);
        Assert.Equal(0.4f, next.VelocityY, 5);
    }

    [Fact]
    public void Grounded_stop_uses_pre_gravity_vertical_velocity_for_full_slowdown()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 8, SolidTile());
        tiles.Set(7, 8, SolidTile());
        using SpawnedServerPlayer player = Spawn(96f, 86f, velocityX: 1f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(
            player.Snapshot,
            ServerPlayerHorizontalIntent.Stop,
            out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(96.8f, next.PositionX, 5);
        Assert.Equal(86f, next.PositionY, 5);
        Assert.Equal(0.8f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.True(next.HitFloor);
    }

    [Fact]
    public void Unknown_horizontal_intent_is_rejected_before_state_changes()
    {
        using SpawnedServerPlayer player = Spawn(96f, 80f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(CreateWorld());

        Assert.False(stepper.TryStep(
            player.Snapshot,
            (ServerPlayerHorizontalIntent)42,
            out _));
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
        var id = new ServerPlayerId("test:horizontal-physics");
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
