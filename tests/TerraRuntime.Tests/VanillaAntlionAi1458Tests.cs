using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaAntlionAi1458Tests
{
    [Fact]
    public void Catalog_admits_source_ai19_antlion_defaults_and_coverage()
    {
        Assert.Equal(1, VanillaAntlionNpcCatalog1458.DefinitionCount);
        Assert.True(VanillaAntlionNpcCatalog1458.TryGetDefinition(VanillaNpcIds.Antlion, out VanillaNpcDefinition definition));
        Assert.Equal((19, 24, 24, 10, 6, 45), (definition.AiStyle.Value, definition.BaseWidth, definition.BaseHeight,
            definition.Damage, definition.Defense, definition.LifeMax));
        Assert.Equal(VanillaNpcBehaviorFamily.Antlion, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.GenericGround, definition.PhysicsFamily);
        Assert.Equal(0f, definition.KnockBackResist);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Antlion, out VanillaNpcDefinition resolved));
        Assert.Equal(definition, resolved);
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.Antlion, out VanillaNpcAiCoverage coverage));
        Assert.True(coverage.Has(VanillaNpcAiCapability.AntlionMotionSlice));
        Assert.True(coverage.Has(VanillaNpcAiCapability.FlyerProjectileSideEffectSlice));
    }

    [Fact]
    public void Upward_visible_target_arms_source_cooldown_and_anchors_to_solid_floor()
    {
        VanillaAntlionMotionResult1458 result = Step(
            velocityX: 4f,
            velocityY: 3f,
            directionY: -1,
            hasTarget: true,
            targetCenterX: 112f,
            targetTopY: 29f,
            playerShotReady: true,
            hasSolidFloor: true);

        Assert.Equal(0f, result.VelocityX);
        Assert.Equal(-.2f, result.VelocityY, 5);
        Assert.Equal(0f, result.Rotation, 5);
        Assert.Equal(200f, result.Ai0);
        Assert.True(result.NoGravity);
        Assert.True(result.NoTileCollide);
        Assert.Equal(VanillaAntlionShotKind1458.Player, result.ShotKind);
    }

    [Fact]
    public void Conveyor_branch_keeps_rotation_and_launches_only_when_player_branch_is_ineligible()
    {
        VanillaAntlionMotionResult1458 result = Step(
            rotation: .4f,
            directionY: 1,
            hasConveyorBelow: true);

        Assert.Equal(.4f, result.Rotation, 5);
        Assert.Equal(200f, result.Ai0);
        Assert.Equal(VanillaAntlionShotKind1458.Conveyor, result.ShotKind);

        VanillaAntlionMotionResult1458 cooldown = Step(ai0: 3f, directionY: 1, hasConveyorBelow: true);
        Assert.Equal(2f, cooldown.Ai0);
        Assert.Equal(VanillaAntlionShotKind1458.None, cooldown.ShotKind);
    }

    [Fact]
    public void Dispatcher_and_post_commit_plan_use_player_top_aim_and_ai_two_sand_projectile()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetAntlionEnvironment(new AntlionEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(2, 112f, 50f, 0, true, false, false, false)]);
        NpcSnapshot antlion = Snapshot();

        Assert.True(stepper.TryStepState(in antlion, out NpcStateUpdate next));
        Assert.Equal((ushort)2, next.Target);
        Assert.Equal(200f, next.Ai.Ai0);
        Assert.True(next.Simulation.NoGravity);

        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in antlion, in next, intents));
        NpcAiProjectileIntent intent = intents[0];
        Assert.Equal(VanillaProjectileIds.AntlionSand, intent.Type);
        Assert.Equal((112f, 112f), (intent.PositionX, intent.PositionY));
        Assert.Equal(0f, intent.VelocityX, 5);
        Assert.Equal(-12f, intent.VelocityY, 5);
        Assert.Equal(new ProjectileAiState(2f, 0f, 0f), intent.InitialAi);
        Assert.Equal(300, intent.TimeLeftOverride);
    }

    [Fact]
    public void Ai010_sand_with_ai_two_uses_antlion_acceleration_not_terrain_block_gravity()
    {
        ProjectileSnapshot projectile = new(
            new ProjectileHandle(1, new ProjectileGeneration(1)), new ProjectileRevision(1), VanillaProjectileIds.AntlionSand,
            VanillaProjectileOwnership.ServerOwner, 0f, 0f, 1f, 2f, new ProjectileAiState(2f, 0f, 0f), 0,
            10, 0f, 10);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(in projectile, in definition, default,
            out VanillaProjectileBehaviorResult next));
        Assert.Equal(.96f, next.VelocityX, 5);
        Assert.Equal(2.2f, next.VelocityY, 5);
        Assert.Equal(2f, next.Ai0);
        Assert.True(next.TileCollideOverride);
    }

    private static VanillaAntlionMotionResult1458 Step(
        float velocityX = 0f,
        float velocityY = 0f,
        float rotation = 0f,
        float ai0 = 0f,
        int directionY = 1,
        bool hasTarget = false,
        float targetCenterX = 0f,
        float targetTopY = 0f,
        bool playerShotReady = false,
        bool hasConveyorBelow = false,
        bool hasSolidFloor = false)
    {
        var input = new VanillaAntlionMotionInput1458(
            velocityX, velocityY, rotation, ai0, 100f, 100f, 24, 24, directionY, hasTarget,
            targetCenterX, targetTopY, playerShotReady, hasConveyorBelow, hasSolidFloor);
        Assert.True(VanillaAntlionMotion1458.TryStep(in input, out VanillaAntlionMotionResult1458 result));
        return result;
    }

    private static NpcSnapshot Snapshot() =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.Antlion.Value,
            checked((short)VanillaNpcIds.Antlion.Value), 100f, 100f, 4f, 3f, byte.MaxValue, default,
            NpcSimulationState.Initial with { Life = 45, LifeMax = 45, DirectionX = 1, DirectionY = 1, Scale = 1f });

    private sealed class AntlionEnvironment : IVanillaAntlionEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;

        public bool HasSolidFloor(float positionX, float positionY, int width, int height) => true;

        public bool HasConveyorBelow(float positionX, float positionY, int width, int height) => false;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
