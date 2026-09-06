using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaBatAi1458Tests
{
    [Fact]
    public void Catalog_admits_source_defaults_for_nine_ordinary_bats_and_slimer()
    {
        Assert.Equal(10, VanillaBatNpcCatalog1458.DefinitionCount);
        foreach (VanillaNpcDefinition definition in VanillaBatNpcCatalog1458.AllDefinitions)
        {
            Assert.Equal(VanillaNpcAiStyles.Bat, definition.AiStyle);
            Assert.Equal(VanillaNpcBehaviorFamily.Bat, definition.BehaviorFamily);
            Assert.Equal(VanillaNpcPhysicsFamily.BatFlight, definition.PhysicsFamily);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(definition.Type, out VanillaNpcDefinition resolved));
            Assert.Equal(definition, resolved);
            Assert.True(VanillaNpcAiCoverageCatalog.TryGet(definition.Type, out VanillaNpcAiCoverage coverage));
            Assert.True(coverage.Has(VanillaNpcAiCapability.BatMotionSlice));
            Assert.False(coverage.FullVanillaAiParity);
        }

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.LavaBat, out VanillaNpcDefinition lavaBat));
        Assert.Equal(22, lavaBat.BaseWidth);
        Assert.Equal(22, lavaBat.BaseHeight);
        Assert.Equal(50, lavaBat.Damage);
        Assert.Equal(16, lavaBat.Defense);
        Assert.Equal(160, lavaBat.LifeMax);
        Assert.Equal(0.6f, lavaBat.KnockBackResist);
        Assert.Equal(1.15f, lavaBat.Scale);
    }

    [Fact]
    public void Slimer_uses_one_generic_ai14_acceleration_pass()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.Slimer,
            directionX: 1,
            directionY: -1);

        Assert.Equal(0.1f, result.VelocityX, 5);
        Assert.Equal(-0.04f, result.VelocityY, 5);
    }

    [Fact]
    public void Purple_queen_slime_minion_uses_its_source_acceleration_profile()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.QueenSlimeMinionPurple,
            directionX: 1,
            directionY: -1);

        Assert.Equal(0.35f, result.VelocityX, 5);
        Assert.Equal(-0.3f, result.VelocityY, 5);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(
            VanillaNpcIds.QueenSlimeMinionPurple,
            out VanillaNpcDefinition definition));
        Assert.Equal(VanillaNpcBehaviorFamily.Bat, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.BatFlight, definition.PhysicsFamily);
    }

    [Fact]
    public void Collision_rebound_precedes_closest_target_acceleration()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.CaveBat,
            velocityX: 0.25f,
            velocityY: 0.25f,
            oldVelocityX: 3f,
            oldVelocityY: -4f,
            directionX: -1,
            directionY: -1,
            collideX: true,
            collideY: true,
            closest: new VanillaBlueSlimeTargetRefresh(true, 7, 1, -1));

        Assert.Equal(-1.4f, result.VelocityX, 5);
        Assert.Equal(1.82f, result.VelocityY, 5);
        Assert.Equal(1, result.DirectionX);
        Assert.Equal(-1, result.DirectionY);
        Assert.Equal((ushort)7, result.Target);
    }

    [Fact]
    public void Hellbat_uses_its_distinct_opposing_velocity_corrections()
    {
        VanillaBatMotionResult1458 cave = Step(
            VanillaNpcIds.CaveBat,
            velocityX: 1f,
            directionX: -1,
            directionY: 0);
        VanillaBatMotionResult1458 hell = Step(
            VanillaNpcIds.Hellbat,
            velocityX: 1f,
            directionX: -1,
            directionY: 0);

        Assert.Equal(0.9f, cave.VelocityX, 5);
        Assert.Equal(0.88f, hell.VelocityX, 5);
    }

    [Fact]
    public void Wet_escape_and_wander_clock_follow_source_order()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.GiantBat,
            velocityY: 3f,
            directionX: 1,
            directionY: 1,
            ai: new NpcAiState(0f, 200f, 150f, 0f),
            wet: true);

        Assert.Equal(201f, result.Ai.Ai1);
        Assert.Equal(151f, result.Ai.Ai2);
        Assert.Equal(0.4f, result.VelocityX, 5);
        Assert.Equal(2.35f, result.VelocityY, 5);
    }

    [Fact]
    public void Dry_visible_target_resets_pursuit_clock_but_still_advances_wander_phase()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.IceBat,
            directionX: 1,
            directionY: -1,
            ai: new NpcAiState(0f, 200f, 0f, 0f),
            targetDryAndVisible: true);

        Assert.Equal(0f, result.Ai.Ai1);
        Assert.Equal(1f, result.Ai.Ai2);
    }

    [Fact]
    public void Dispatcher_routes_bat_family_and_commits_no_gravity_state()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(7, 200f, 40f, 0, true, false, false, false)
        ]);
        NpcSnapshot bat = Snapshot(VanillaNpcIds.CaveBat) with
        {
            Ai = new NpcAiState(0f, 200f, 0f, 0f)
        };

        Assert.True(stepper.TryStepState(in bat, out NpcStateUpdate next));

        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(0f, next.Ai.Ai1);
        Assert.True(next.Simulation.NoGravity);
        Assert.False(next.Simulation.NoTileCollide);
    }

    private static VanillaBatMotionResult1458 Step(
        NpcTypeId type,
        float velocityX = 0f,
        float velocityY = 0f,
        float oldVelocityX = 0f,
        float oldVelocityY = 0f,
        int directionX = 0,
        int directionY = 0,
        bool collideX = false,
        bool collideY = false,
        bool wet = false,
        NpcAiState ai = default,
        VanillaBlueSlimeTargetRefresh closest = default,
        bool targetDryAndVisible = false)
    {
        var input = new VanillaBatMotionInput1458(
            velocityX,
            velocityY,
            oldVelocityX,
            oldVelocityY,
            directionX,
            directionY,
            byte.MaxValue,
            ai,
            wet,
            collideX,
            collideY,
            closest,
            targetDryAndVisible);
        Assert.True(VanillaBatMotion1458.TryStep(type, in input, out VanillaBatMotionResult1458 result));
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
                Life = 16,
                LifeMax = 16,
                DirectionX = 1,
                DirectionY = 1,
                Scale = 1f
            });

    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(
            float sourcePositionX,
            float sourcePositionY,
            int sourceWidth,
            int sourceHeight,
            float targetPositionX,
            float targetPositionY,
            int targetWidth,
            int targetHeight) => true;
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
