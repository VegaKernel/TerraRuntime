using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaFishAi1458Tests
{
    [Fact]
    public void Catalog_admits_all_thirteen_ai16_definitions()
    {
        Assert.Equal(13, VanillaFishNpcCatalog1458.DefinitionCount);
        foreach (VanillaNpcDefinition definition in VanillaFishNpcCatalog1458.AllDefinitions)
        {
            Assert.Equal(VanillaNpcAiStyles.Fish, definition.AiStyle);
            Assert.Equal(VanillaNpcBehaviorFamily.Fish, definition.BehaviorFamily);
            Assert.Equal(VanillaNpcPhysicsFamily.FishSwimming, definition.PhysicsFamily);
            Assert.True(definition.NoGravityAtSpawn);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(definition.Type, out VanillaNpcDefinition resolved));
            Assert.Equal(definition, resolved);
            Assert.True(VanillaNpcAiCoverageCatalog.TryGet(definition.Type, out VanillaNpcAiCoverage coverage));
            Assert.True(coverage.Has(VanillaNpcAiCapability.FishMotionSlice));
        }

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Orca, out VanillaNpcDefinition orca));
        Assert.Equal(120, orca.BaseWidth);
        Assert.Equal(34, orca.BaseHeight);
        Assert.Equal(50, orca.Damage);
        Assert.Equal(20, orca.Defense);
        Assert.Equal(400, orca.LifeMax);
    }

    [Theory]
    [InlineData(55, 20, 18, 0, 0, 5, 0.5f)]
    [InlineData(57, 18, 20, 30, 6, 100, 1f)]
    [InlineData(58, 18, 20, 25, 2, 30, 1f)]
    [InlineData(65, 100, 24, 40, 2, 300, 0.7f)]
    [InlineData(102, 18, 20, 80, 22, 90, 1f)]
    [InlineData(157, 74, 20, 75, 30, 200, 1f)]
    [InlineData(241, 18, 20, 50, 20, 150, 1f)]
    [InlineData(465, 18, 20, 31, 7, 110, 1f)]
    [InlineData(592, 20, 18, 0, 0, 5, 0.5f)]
    [InlineData(607, 20, 18, 0, 0, 5, 0.5f)]
    [InlineData(615, 20, 18, 0, 0, 5, 0.5f)]
    [InlineData(688, 32, 16, 0, 0, 5, 0.5f)]
    [InlineData(692, 120, 34, 50, 20, 400, 0.7f)]
    public void Every_ai16_definition_matches_source_defaults(
        int type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist)
    {
        Assert.True(VanillaFishNpcCatalog1458.TryGetDefinition(new NpcTypeId(type), out VanillaNpcDefinition definition));
        Assert.Equal(width, definition.BaseWidth);
        Assert.Equal(height, definition.BaseHeight);
        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(lifeMax, definition.LifeMax);
        Assert.Equal(knockBackResist, definition.KnockBackResist);
    }

    [Fact]
    public void Wet_piranha_pursues_visible_wet_target_with_common_caps()
    {
        VanillaFishMotionResult1458 result = Step(
            VanillaNpcIds.Piranha,
            velocityX: 2.95f,
            velocityY: -1.95f,
            directionX: -1,
            directionY: 1,
            wet: true,
            closest: new VanillaBlueSlimeTargetRefresh(true, 7, 1, -1),
            pursuingWetTarget: true,
            ai: new NpcAiState(1f, 0f, 0f, 0f));

        Assert.Equal(3f, result.VelocityX, 5);
        Assert.Equal(-2f, result.VelocityY, 5);
        Assert.Equal(0f, result.Ai.Ai0);
        Assert.Equal((ushort)7, result.Target);
        Assert.Equal(1, result.DirectionX);
        Assert.Equal(-1, result.DirectionY);
    }

    [Fact]
    public void Arapaima_reversal_and_source_overspeed_snap_are_preserved()
    {
        VanillaFishMotionResult1458 result = Step(
            VanillaNpcIds.Arapaima,
            velocityX: 1f,
            velocityY: 4.9f,
            directionX: 1,
            directionY: -1,
            wet: true,
            closest: new VanillaBlueSlimeTargetRefresh(true, 2, -1, 1),
            pursuingWetTarget: true);

        Assert.Equal(0.7f, result.VelocityX, 5);
        Assert.Equal(4f, result.VelocityY, 5);
    }

    [Fact]
    public void Dry_fish_uses_server_rng_flop_then_gravity_increment()
    {
        VanillaFishMotionResult1458 result = Step(
            VanillaNpcIds.Piranha,
            directionX: 1,
            hasGroundedLaunch: true,
            groundedLaunchVelocityX: -1.2f,
            groundedLaunchVelocityY: -3f,
            groundedLaunchDirection: -1);

        Assert.Equal(-1.2f, result.VelocityX, 5);
        Assert.Equal(-2.7f, result.VelocityY, 5);
        Assert.Equal(-1, result.DirectionX);
        Assert.Equal(1f, result.Ai.Ai0);
    }

    [Fact]
    public void Grounded_shark_damps_to_zero_without_consuming_flop()
    {
        VanillaFishMotionResult1458 result = Step(VanillaNpcIds.Shark, velocityX: 0.21f);

        Assert.Equal(0f, result.VelocityX);
        Assert.Equal(0.3f, result.VelocityY, 5);
    }

    [Fact]
    public void Dolphin_alone_uses_the_source_three_pixel_idle_speed_threshold()
    {
        VanillaFishMotionResult1458 dolphin = Step(
            VanillaNpcIds.Dolphin,
            velocityX: 2.9f,
            directionX: 1,
            wet: true);
        VanillaFishMotionResult1458 pupfish = Step(
            VanillaNpcIds.Pupfish,
            velocityX: 2.9f,
            directionX: 1,
            wet: true);

        Assert.Equal(3f, dolphin.VelocityX, 5);
        Assert.Equal(2.85f, pupfish.VelocityX, 5);
    }

    [Fact]
    public void Pufferfish_hit_inflates_and_follows_water_line()
    {
        VanillaFishMotionResult1458 result = Step(
            VanillaNpcIds.Pufferfish,
            velocityX: 2f,
            velocityY: 1f,
            positionY: 100f,
            centerY: 108f,
            wet: true,
            justHit: true,
            hasWaterLine: true,
            waterLineHeight: 90f);

        Assert.Equal(1.96f, result.VelocityX, 5);
        Assert.Equal(0.58f, result.VelocityY, 5);
        Assert.Equal(1f, result.Ai.Ai2);
        Assert.Equal(180f, result.LocalAi.Ai0);
    }

    [Fact]
    public void Pufferfish_timer_expiry_deflates_before_common_swimming()
    {
        VanillaFishMotionResult1458 result = Step(
            VanillaNpcIds.Pufferfish,
            directionX: 1,
            wet: true,
            ai: new NpcAiState(0f, 0f, 1f, 0f),
            localAi: new NpcAiState(1f, 0f, 0f, 0f));

        Assert.Equal(0f, result.Ai.Ai2);
        Assert.Equal(120f, result.LocalAi.Ai0);
        Assert.Equal(0.1f, result.VelocityX, 5);
        Assert.Equal(0.01f, result.VelocityY, 5);
    }

    [Fact]
    public void Dolphin_blocked_breach_switches_to_surface_state_and_tracks_water_line()
    {
        VanillaFishMotionResult1458 result = Step(
            VanillaNpcIds.Dolphin,
            velocityX: 2f,
            positionY: 100f,
            centerY: 109f,
            directionX: -1,
            directionY: 1,
            wet: true,
            ai: new NpcAiState(0f, 0f, 0f, 1198f),
            closest: new VanillaBlueSlimeTargetRefresh(true, 4, 1, -1),
            dolphinWaitThreshold: 1199,
            dolphinNextState: 1,
            dolphinCanHitLineAbove: false,
            hasWaterLine: true,
            waterLineHeight: 105f);

        Assert.Equal(2f, result.Ai.Ai2);
        Assert.Equal(1f, result.Ai.Ai3);
        Assert.Equal(1.9f, result.VelocityX, 5);
        Assert.Equal(0.5f, result.VelocityY, 5);
        Assert.Equal((ushort)4, result.Target);
    }

    [Fact]
    public void Dolphin_breach_marks_air_crossing_then_resets_after_reentering_water()
    {
        VanillaFishMotionResult1458 airborne = Step(
            VanillaNpcIds.Dolphin,
            velocityY: -1f,
            ai: new NpcAiState(0f, 0f, 1f, 0f));
        Assert.Equal(-0.7f, airborne.VelocityY, 5);
        Assert.Equal(1f, airborne.Ai.Ai3);

        VanillaFishMotionResult1458 reentered = Step(
            VanillaNpcIds.Dolphin,
            velocityY: -0.7f,
            wet: true,
            ai: airborne.Ai);
        Assert.Equal(0f, reentered.Ai.Ai2);
        Assert.Equal(0f, reentered.Ai.Ai3);
    }

    [Fact]
    public void Production_environment_resolves_slope_depth_and_water_line()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var environment = new VanillaFishWorldEnvironment1458(tiles);
        WorldTile slope = default;
        slope.Shape = 3;
        tiles.Set(20, 21, in slope);
        WorldTile upperWater = default;
        upperWater.LiquidAmount = 160;
        tiles.Set(20, 19, in upperWater);
        WorldTile floor = default;
        floor.Flags = WorldTileFlags.Active;
        floor.Type = 1;
        tiles.Set(20, 22, in floor);

        Assert.Equal(
            VanillaFishBottomSlope1458.FaceLeft,
            environment.GetBottomSlope(320f, 320f, 20, 16));
        Assert.True(environment.HasDeepLiquidAboveAndActiveTileBelow(320f, 312f, 20, 16));
        Assert.True(environment.TryGetWaterLineAtTop(320f, 320f, 20, out float waterLine));
        Assert.Equal(310f, waterLine);
    }

    [Fact]
    public void Production_dolphin_clearance_uses_vanilla_can_hit_line()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var environment = new VanillaFishWorldEnvironment1458(tiles);
        WorldTile solid = default;
        solid.Flags = WorldTileFlags.Active;
        solid.Type = 1;
        tiles.Set(20, 16, in solid);

        Assert.False(environment.CanHitLineStraightAbove(320f, 320f, 20, 18, 128f));

        WorldTile platform = solid;
        platform.Type = checked((ushort)VanillaTileIds.Platforms.Value);
        tiles.Set(20, 16, in platform);
        Assert.True(environment.CanHitLineStraightAbove(320f, 320f, 20, 18, 128f));
    }

    [Fact]
    public void Dispatcher_routes_fish_and_commits_local_ai_and_no_gravity()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new MinimumRandom());
        stepper.SetFishEnvironment(new VisibleFishEnvironment());
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(3, 200f, 40f, 0, true, false, false, false) { Wet = true }
        ]);
        NpcSnapshot fish = Snapshot(VanillaNpcIds.Piranha) with
        {
            Simulation = Snapshot(VanillaNpcIds.Piranha).Simulation with { Wet = true }
        };

        Assert.True(stepper.TryStepState(in fish, out NpcStateUpdate next));
        Assert.Equal((ushort)3, next.Target);
        Assert.True(next.Simulation.NoGravity);
        Assert.False(next.Simulation.NoTileCollide);
    }

    private static VanillaFishMotionResult1458 Step(
        NpcTypeId type,
        float velocityX = 0f,
        float velocityY = 0f,
        float positionY = 100f,
        float centerY = 110f,
        int directionX = 0,
        int directionY = 0,
        bool wet = false,
        bool collideX = false,
        bool collideY = false,
        bool justHit = false,
        NpcAiState ai = default,
        NpcAiState localAi = default,
        VanillaBlueSlimeTargetRefresh closest = default,
        bool pursuingWetTarget = false,
        bool hasGroundedLaunch = false,
        float groundedLaunchVelocityX = 0f,
        float groundedLaunchVelocityY = 0f,
        int groundedLaunchDirection = 1,
        int dolphinWaitThreshold = 300,
        int dolphinNextState = 1,
        bool dolphinCanHitLineAbove = true,
        bool hasWaterLine = false,
        float waterLineHeight = 0f)
    {
        var input = new VanillaFishMotionInput1458(
            velocityX,
            velocityY,
            positionY,
            centerY,
            directionX,
            directionY,
            byte.MaxValue,
            ai,
            localAi,
            justHit,
            wet,
            collideX,
            collideY,
            VanillaFishBottomSlope1458.None,
            DeepLiquidAboveAndActiveTileBelow: false,
            closest,
            pursuingWetTarget,
            TargetTopBelowNpcTop: false,
            hasGroundedLaunch,
            groundedLaunchVelocityX,
            groundedLaunchVelocityY,
            groundedLaunchDirection,
            dolphinWaitThreshold,
            dolphinNextState,
            dolphinCanHitLineAbove,
            hasWaterLine,
            waterLineHeight);
        Assert.True(VanillaFishMotion1458.TryStep(type, in input, out VanillaFishMotionResult1458 result));
        return result;
    }

    private static NpcSnapshot Snapshot(NpcTypeId type) =>
        new(
            new NpcHandle(1, new NpcGeneration(1)),
            new NpcRevision(1),
            type.Value,
            checked((short)type.Value),
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: byte.MaxValue,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                Life = 30,
                LifeMax = 30,
                DirectionX = 1,
                DirectionY = 1,
                Scale = 1f
            });

    private sealed class VisibleFishEnvironment : IVanillaFishEnvironment1458
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;

        public VanillaFishBottomSlope1458 GetBottomSlope(
            float positionX, float positionY, int width, int height) => VanillaFishBottomSlope1458.None;

        public bool HasDeepLiquidAboveAndActiveTileBelow(
            float positionX, float positionY, int width, int height) => false;

        public bool CanHitLineStraightAbove(
            float positionX, float positionY, int width, int height, float distance) => true;

        public bool TryGetWaterLineAtTop(
            float positionX, float positionY, int width, out float waterLineHeight)
        {
            waterLineHeight = 0f;
            return false;
        }
    }

    private sealed class MinimumRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
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
