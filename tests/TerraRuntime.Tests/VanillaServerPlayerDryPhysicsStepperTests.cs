using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaServerPlayerDryPhysicsStepperTests
{
    [Fact]
    public void Empty_air_applies_source_backed_gravity_and_advances_position()
    {
        WorldTileStore tiles = CreateWorld();
        using SpawnedServerPlayer player = Spawn(positionX: 96f, positionY: 80f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(player.Snapshot, out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(96f, next.PositionX, 5);
        Assert.Equal(80.4f, next.PositionY, 5);
        Assert.Equal(0f, next.VelocityX, 5);
        Assert.Equal(0.4f, next.VelocityY, 5);
        Assert.False(next.CollideX);
        Assert.False(next.CollideY);
    }

    [Theory]
    [InlineData(WorldLiquidKind.Water, 0.5f)]
    [InlineData(WorldLiquidKind.Lava, 0.5f)]
    [InlineData(WorldLiquidKind.Honey, 0.25f)]
    [InlineData(WorldLiquidKind.Shimmer, 0.375f)]
    public void Liquid_contact_scales_position_advance_without_scaling_authoritative_velocity(
        WorldLiquidKind liquidKind,
        float movementScale)
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, LiquidTile(liquidKind));
        using SpawnedServerPlayer player = Spawn(positionX: 96f, positionY: 80f, velocityX: 2f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(player.Snapshot, out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(96f + 2f * movementScale, next.PositionX, 5);
        Assert.Equal(80f + 0.4f * movementScale, next.PositionY, 5);
        Assert.Equal(2f, next.VelocityX, 5);
        Assert.Equal(0.4f, next.VelocityY, 5);
        Assert.False(next.CollideX);
        Assert.False(next.CollideY);
    }

    [Fact]
    public void Liquid_scale_does_not_dilute_a_tile_collision_clamp()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        for (int y = 5; y <= 7; y++)
            tiles.Set(8, y, SolidTile());
        using SpawnedServerPlayer player = Spawn(positionX: 100f, positionY: 80f, velocityX: 20f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(player.Snapshot, out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(108f, next.PositionX, 5);
        Assert.Equal(80.2f, next.PositionY, 5);
        Assert.Equal(8f, next.VelocityX, 5);
        Assert.Equal(0.4f, next.VelocityY, 5);
        Assert.True(next.CollideX);
        Assert.False(next.CollideY);
    }

    [Fact]
    public void Falling_speed_is_clamped_to_source_backed_vanilla_terminal_speed()
    {
        WorldTileStore tiles = CreateWorld();
        using SpawnedServerPlayer player = Spawn(positionX: 96f, positionY: 80f, velocityY: 9.9f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(player.Snapshot, out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(90.01f, next.PositionY, 5);
        Assert.Equal(10.01f, next.VelocityY, 5);
    }

    [Fact]
    public void Solid_floor_consumes_gravity_without_moving_player_through_tile()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 8, SolidTile());
        tiles.Set(7, 8, SolidTile());
        using SpawnedServerPlayer player = Spawn(positionX: 96f, positionY: 86f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(player.Snapshot, out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(86f, next.PositionY, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.True(next.CollideY);
        Assert.True(next.HitFloor);
    }

    [Fact]
    public void Solid_wall_clamps_horizontal_motion_using_player_hitbox()
    {
        WorldTileStore tiles = CreateWorld();
        for (int y = 5; y <= 7; y++)
            tiles.Set(8, y, SolidTile());
        using SpawnedServerPlayer player = Spawn(positionX: 100f, positionY: 80f, velocityX: 20f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        Assert.True(stepper.TryStep(player.Snapshot, out ServerPlayerDryPhysicsStepResult next));

        Assert.Equal(108f, next.PositionX, 5);
        Assert.Equal(8f, next.VelocityX, 5);
        Assert.True(next.CollideX);
    }

    [Fact]
    public void Dead_and_mounted_players_are_outside_the_verified_dry_slice()
    {
        WorldTileStore tiles = CreateWorld();
        using SpawnedServerPlayer player = Spawn(positionX: 96f, positionY: 80f);
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);

        PlayerStateSnapshot dead = player.Snapshot with { IsDead = true };
        PlayerStateSnapshot mounted = player.Snapshot with { MountType = 1 };

        Assert.False(stepper.TryStep(in dead, out _));
        Assert.False(stepper.TryStep(in mounted, out _));
    }

    [Fact]
    public void Constants_pin_the_verified_terraria_server_1458_baseline()
    {
        Assert.Equal(20, VanillaServerPlayerDryPhysicsStepper.PlayerWidth);
        Assert.Equal(42, VanillaServerPlayerDryPhysicsStepper.PlayerHeight);
        Assert.Equal(0.4f, VanillaServerPlayerDryPhysicsStepper.Gravity);
        Assert.Equal(10f, VanillaServerPlayerDryPhysicsStepper.MaximumFallSpeed);
        Assert.Equal(0.01f, VanillaServerPlayerPhysicsProfile.FallSpeedNudge);
        Assert.Equal(0.5f, VanillaServerPlayerLiquidMovement.WaterMovementScale);
        Assert.Equal(0.5f, VanillaServerPlayerLiquidMovement.LavaMovementScale);
        Assert.Equal(0.25f, VanillaServerPlayerLiquidMovement.HoneyMovementScale);
        Assert.Equal(0.375f, VanillaServerPlayerLiquidMovement.ShimmerMovementScale);
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(100, 100));

    private static WorldTile SolidTile() =>
        new()
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        };

    private static WorldTile LiquidTile(WorldLiquidKind liquidKind) =>
        new()
        {
            LiquidAmount = byte.MaxValue,
            LiquidKind = liquidKind
        };

    private static SpawnedServerPlayer Spawn(
        float positionX,
        float positionY,
        float velocityX = 0f,
        float velocityY = 0f)
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:physics");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
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
        RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease lease,
        PlayerStateSnapshot snapshot) : IDisposable
    {
        public PlayerStateSnapshot Snapshot { get; } = snapshot;

        public void Dispose() => lease.Dispose();
    }
}
