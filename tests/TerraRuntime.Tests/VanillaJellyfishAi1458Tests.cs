using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaJellyfishAi1458Tests
{
    [Fact]
    public void Catalog_admits_the_source_ai18_jellyfish_defaults()
    {
        Assert.Equal(1, VanillaJellyfishNpcCatalog1458.DefinitionCount);
        Assert.True(VanillaJellyfishNpcCatalog1458.TryGetDefinition(VanillaNpcIds.Jellyfish, out VanillaNpcDefinition definition));
        Assert.Equal(VanillaNpcAiStyles.Jellyfish, definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.Jellyfish, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.Jellyfish, definition.PhysicsFamily);
        Assert.Equal(26, definition.BaseWidth);
        Assert.Equal(26, definition.BaseHeight);
        Assert.Equal(90, definition.Damage);
        Assert.Equal(20, definition.Defense);
        Assert.Equal(140, definition.LifeMax);
        Assert.Equal(20, definition.AlphaAtSpawn);
        Assert.True(definition.NoGravityAtSpawn);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Jellyfish, out VanillaNpcDefinition resolved));
        Assert.Equal(definition, resolved);
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.Jellyfish, out VanillaNpcAiCoverage coverage));
        Assert.True(coverage.Has(VanillaNpcAiCapability.JellyfishMotionSlice));
    }

    [Fact]
    public void Wet_jellyfish_retargets_visible_wet_player_and_uses_source_seven_pixel_charge()
    {
        VanillaJellyfishMotionResult1458 result = Step(
            velocityX: .1f,
            velocityY: -.1f,
            wet: true,
            closest: new VanillaBlueSlimeTargetRefresh(true, 4, 1, 1),
            closestWetAndVisible: true,
            closestCenterX: 113f,
            closestCenterY: 13f);

        Assert.Equal((ushort)4, result.Target);
        Assert.Equal(7f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
    }

    [Fact]
    public void Wet_jellyfish_preserves_slope_turn_depth_control_and_collision_response()
    {
        VanillaJellyfishMotionResult1458 result = Step(
            velocityX: 1f,
            velocityY: .5f,
            directionX: 1,
            ai: new NpcAiState(-1f, 0f, 0f, 0f),
            wet: true,
            bottomSlope: VanillaFishBottomSlope1458.FaceLeft,
            deepLiquidAboveAndActiveTileBelow: true);

        Assert.Equal(-.969f, result.VelocityX, 5);
        Assert.Equal(.49f, result.VelocityY, 5);
        Assert.Equal(-1, result.DirectionX);
        Assert.Equal(-1f, result.Ai.Ai0);

        VanillaJellyfishMotionResult1458 ceiling = Step(
            velocityY: -1.5f,
            directionY: 1,
            wet: true,
            collideY: true);
        Assert.Equal(1.4949f, ceiling.VelocityY, 5);
        Assert.Equal(1, ceiling.DirectionY);
        Assert.Equal(1f, ceiling.Ai.Ai0);
    }

    [Fact]
    public void Dry_jellyfish_uses_source_horizontal_stop_and_fall_cap()
    {
        VanillaJellyfishMotionResult1458 result = Step(velocityX: .005f, velocityY: 0f);

        Assert.Equal(0f, result.VelocityX);
        Assert.Equal(.2f, result.VelocityY, 5);
        Assert.Equal(1f, result.Ai.Ai0);
    }

    [Fact]
    public void Dispatcher_routes_jellyfish_through_the_shared_tile_environment()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetFishEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(3, 200f, 33f, 0, true, false, false, false) { Wet = true }
        ]);
        NpcSnapshot jellyfish = Snapshot() with
        {
            Simulation = Snapshot().Simulation with { Wet = true }
        };

        Assert.True(stepper.TryStepState(in jellyfish, out NpcStateUpdate next));
        Assert.Equal((ushort)3, next.Target);
        Assert.True(next.Simulation.NoGravity);
        Assert.False(next.Simulation.NoTileCollide);
        Assert.True(next.VelocityX > 0f);
    }

    private static VanillaJellyfishMotionResult1458 Step(
        float velocityX = 0f,
        float velocityY = 0f,
        int directionX = 1,
        int directionY = 1,
        NpcAiState ai = default,
        bool wet = false,
        bool collideX = false,
        bool collideY = false,
        VanillaFishBottomSlope1458 bottomSlope = VanillaFishBottomSlope1458.None,
        bool deepLiquidAboveAndActiveTileBelow = false,
        VanillaBlueSlimeTargetRefresh closest = default,
        bool closestWetAndVisible = false,
        float closestCenterX = 0f,
        float closestCenterY = 0f)
    {
        var input = new VanillaJellyfishMotionInput1458(
            velocityX, velocityY, 0f, 0f, 26, 26, directionX, directionY, byte.MaxValue, ai, wet,
            collideX, collideY, Friendly: false, bottomSlope, deepLiquidAboveAndActiveTileBelow, closest,
            closestWetAndVisible, closestCenterX, closestCenterY);
        Assert.True(VanillaJellyfishMotion1458.TryStep(in input, out VanillaJellyfishMotionResult1458 result));
        return result;
    }

    private static NpcSnapshot Snapshot() =>
        new(
            new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.Jellyfish.Value,
            checked((short)VanillaNpcIds.Jellyfish.Value), 0f, 0f, 0f, 0f, byte.MaxValue, default,
            NpcSimulationState.Initial with { Life = 140, LifeMax = 140, DirectionX = 1, DirectionY = 1, Scale = 1f });

    private sealed class VisibleEnvironment : IVanillaFishEnvironment1458
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;

        public VanillaFishBottomSlope1458 GetBottomSlope(float positionX, float positionY, int width, int height) =>
            VanillaFishBottomSlope1458.None;

        public bool HasDeepLiquidAboveAndActiveTileBelow(float positionX, float positionY, int width, int height) => false;

        public bool CanHitLineStraightAbove(float positionX, float positionY, int width, int height, float distance) => true;

        public bool TryGetWaterLineAtTop(float positionX, float positionY, int width, out float waterLineHeight)
        {
            waterLineHeight = 0f;
            return false;
        }
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
