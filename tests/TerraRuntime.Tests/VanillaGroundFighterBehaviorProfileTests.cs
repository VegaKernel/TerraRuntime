using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaGroundFighterBehaviorProfileTests
{
    [Fact]
    public void Admitted_ground_fighters_have_complete_valid_profiles()
    {
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(
            VanillaNpcIds.Zombie,
            out VanillaGroundFighterBehaviorParameters zombie));
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(
            VanillaNpcIds.Skeleton,
            out VanillaGroundFighterBehaviorParameters skeleton));

        Assert.True(zombie.IsValid);
        Assert.True(skeleton.IsValid);
        Assert.Equal(1f, zombie.BaseMaximumHorizontalSpeed);
        Assert.Equal(1.5f, skeleton.BaseMaximumHorizontalSpeed);
        Assert.Equal(0.07f, zombie.HorizontalAcceleration, 5);
        Assert.Equal(0.07f, skeleton.HorizontalAcceleration, 5);
        Assert.Equal(60f, zombie.StuckThreshold, 5);
        Assert.Equal(600f, zombie.MaximumStuckCounter, 5);
        Assert.Equal(10, zombie.EncouragedDespawnTime);
        Assert.Equal(-5f, zombie.StuckHopVelocity, 5);
        Assert.Equal(-6f, zombie.OneTileJumpVelocity, 5);
        Assert.Equal(-7f, zombie.TwoTileJumpVelocity, 5);
        Assert.Equal(-8f, zombie.ThreeTileJumpVelocity, 5);
        Assert.Equal(-8f, zombie.PursuitGapJumpVelocity, 5);
        Assert.Equal(1.5f, zombie.PursuitGapSpeedMultiplier, 5);
    }

    [Fact]
    public void Fighter_motion_consumes_configured_acceleration()
    {
        var input = new VanillaZombieMotionInput(
            PositionX: 100f,
            OldPositionX: 99f,
            VelocityX: 0f,
            VelocityY: 0f,
            DirectionX: 1,
            DirectionY: 1,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Scale: 1f,
            TargetOverlaps: false,
            ClosestTarget: new VanillaZombieTargetRefresh(true, 3, 1, 1))
        {
            BaseMaximumHorizontalSpeed = 2f,
            HorizontalAcceleration = 0.2f,
            StuckThreshold = 60f,
            MaximumStuckCounter = 600f,
            EncouragedDespawnTime = 10,
            TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(0.2f, result.VelocityX, 5);
        Assert.Equal(1, result.TargetRefreshes);
        Assert.Equal((ushort)3, result.Target);
    }

    [Fact]
    public void Fighter_motion_consumes_configured_stuck_threshold_and_despawn_window()
    {
        var input = new VanillaZombieMotionInput(
            PositionX: 100f,
            OldPositionX: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            DirectionX: 1,
            DirectionY: 1,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: new NpcAiState(0f, 0f, 0f, 2f),
            Scale: 1f,
            TargetOverlaps: false,
            ClosestTarget: new VanillaZombieTargetRefresh(true, 3, 1, 1))
        {
            BaseMaximumHorizontalSpeed = 1f,
            HorizontalAcceleration = 0.07f,
            StuckThreshold = 2f,
            MaximumStuckCounter = 20f,
            EncouragedDespawnTime = 4,
            PursuitAllowed = true,
            EncourageDespawn = true,
            TimeLeft = 100
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(0, result.TargetRefreshes);
        Assert.Equal(4, result.TimeLeft);
        Assert.Equal(3f, result.Ai.Ai3, 5);
    }

    [Fact]
    public void Obstacle_motion_consumes_configured_jump_profile()
    {
        WorldTileStore oneTileWorld = CreateWorld();
        oneTileWorld.Set(6, 7, SolidTile());
        oneTileWorld.Set(7, 5, SolidTile());
        var parameters = new VanillaZombieObstacleMotionParameters(
            LowStepJumpVelocity: -4f,
            OneTileJumpVelocity: -9f,
            TwoTileJumpVelocity: -7f,
            ThreeTileJumpVelocity: -8f,
            PursuitGapJumpVelocity: -10f,
            PursuitGapSpeedMultiplier: 2f);

        VanillaZombieObstacleMotionResult oneTile = Resolve(oneTileWorld, parameters, directionY: 0);

        Assert.True(oneTile.Jumped);
        Assert.Equal(-9f, oneTile.VelocityY, 5);

        WorldTileStore gapWorld = CreateWorld();
        gapWorld.Set(6, 7, SolidTile());
        VanillaZombieObstacleMotionResult gap = Resolve(gapWorld, parameters, directionY: -1);

        Assert.True(gap.Jumped);
        Assert.Equal(-10f, gap.VelocityY, 5);
        Assert.Equal(1f, gap.VelocityX, 5);
    }

    [Fact]
    public void Coverage_catalog_marks_configured_ground_fighter_traversal()
    {
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.Zombie,
            out VanillaNpcAiCoverage zombie));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.Skeleton,
            out VanillaNpcAiCoverage skeleton));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.DemonEye,
            out VanillaNpcAiCoverage demonEye));

        Assert.True(zombie.Has(VanillaNpcAiCapability.GroundFighterTraversalSlice));
        Assert.True(skeleton.Has(VanillaNpcAiCapability.GroundFighterTraversalSlice));
        Assert.False(demonEye.Has(VanillaNpcAiCapability.GroundFighterTraversalSlice));
    }

    private static VanillaZombieObstacleMotionResult Resolve(
        WorldTileStore tiles,
        VanillaZombieObstacleMotionParameters parameters,
        int directionY) =>
        VanillaWorldZombieObstacleMotion.Resolve(
            tiles,
            positionX: 96f,
            positionY: 80f,
            velocityX: 0.5f,
            velocityY: 0f,
            width: 18,
            height: 40,
            directionX: 1,
            directionY: directionY,
            parameters: parameters);

    private static WorldTileStore CreateWorld() => new(new WorldDimensions(100, 100));

    private static WorldTile SolidTile() => new()
    {
        Type = 1,
        Flags = WorldTileFlags.Active
    };
}
