using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaMoonEventUnicornAiTests
{
    [Fact]
    public void Type_329_defaults_coverage_and_ai_family_match_source()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(329), out VanillaNpcDefinition definition));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(329), out VanillaNpcAiCoverage coverage));

        Assert.Equal(new NpcAiStyleId(26), definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.MoonEventUnicorn, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.UnicornGround, definition.PhysicsFamily);
        Assert.Equal(46, definition.BaseWidth);
        Assert.Equal(30, definition.BaseHeight);
        Assert.Equal(80, definition.Damage);
        Assert.Equal(38, definition.Defense);
        Assert.Equal(1800, definition.LifeMax);
        Assert.Equal(.3f, definition.KnockBackResist);
        Assert.True(coverage.Has(VanillaNpcAiCapability.UnicornTraversalSlice));
    }

    [Fact]
    public void Active_pumpkin_moon_refreshes_target_and_accelerates_to_three_pixels_per_tick()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(pumpkinMoonActive: true, targetX: 300f);
        NpcSnapshot npc = CreateNpc();

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(1, next.Simulation.DirectionX);
        Assert.Equal(.1f, next.VelocityX, 5);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, next.Simulation.TimeLeft);
    }

    [Fact]
    public void Missing_pumpkin_moon_encourages_despawn_but_retains_ai26_motion()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(pumpkinMoonActive: false, targetX: 300f);
        NpcSnapshot npc = CreateNpc();

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(10, next.Simulation.TimeLeft);
        Assert.Equal(.1f, next.VelocityX, 5);
    }

    [Fact]
    public void Close_target_lunge_is_applied_before_ai26_horizontal_speed_control()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(pumpkinMoonActive: true, targetX: 160f);
        NpcSnapshot npc = CreateNpc() with { VelocityX = 3.2f };

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(3.2f, next.VelocityX, 5);
        Assert.Equal(-4f, next.VelocityY, 5);
    }

    [Fact]
    public void Unicorn_obstacle_slice_uses_source_ai26_jump_heights()
    {
        WorldTileStore tiles = new(new WorldDimensions(100, 100));
        tiles.Set(10, 4, SolidTile());
        VanillaZombieObstacleMotionResult oneTile = ResolveObstacle(tiles);

        Assert.True(oneTile.Jumped);
        Assert.Equal(-7f, oneTile.VelocityY, 5);

        tiles.Set(10, 3, SolidTile());
        VanillaZombieObstacleMotionResult twoTile = ResolveObstacle(tiles);
        Assert.Equal(-7.5f, twoTile.VelocityY, 5);

        tiles.Set(10, 2, SolidTile());
        VanillaZombieObstacleMotionResult threeTile = ResolveObstacle(tiles);
        Assert.Equal(-8.5f, threeTile.VelocityY, 5);
    }

    [Fact]
    public void Unicorn_gap_jump_requires_upward_intent_or_fast_charge()
    {
        WorldTileStore tiles = new(new WorldDimensions(100, 100));

        VanillaZombieObstacleMotionResult walking = ResolveObstacle(tiles, velocityX: .5f, directionY: 0);
        VanillaZombieObstacleMotionResult charging = ResolveObstacle(tiles, velocityX: 3.1f, directionY: 0);

        Assert.False(walking.Jumped);
        Assert.True(charging.Jumped);
        Assert.Equal(-8f, charging.VelocityY, 5);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool pumpkinMoonActive, float targetX)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(100d);
        stepper.SetMoonEventState(pumpkinMoonActive);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, targetX, 115f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot CreateNpc() =>
        new(
            new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 329, 329,
            100f, 100f, 0f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget, default,
            NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                SpriteDirection = 1,
                OldPositionX = 99f,
                Life = 1800,
                LifeMax = 1800,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });

    private static VanillaZombieObstacleMotionResult ResolveObstacle(
        WorldTileStore tiles, float velocityX = 3.2f, int directionY = 0) =>
        VanillaWorldUnicornObstacleMotion.Resolve(
            tiles, positionX: 96f, positionY: 80f, velocityX, velocityY: 0f,
            width: 46, height: 30, directionX: 1, directionY, spriteDirection: 1);

    private static WorldTile SolidTile() => new() { Type = 1, Flags = WorldTileFlags.Active };

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
