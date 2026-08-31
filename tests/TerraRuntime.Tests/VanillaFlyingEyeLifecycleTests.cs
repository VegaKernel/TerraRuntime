using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaFlyingEyeLifecycleTests
{
    [Fact]
    public void Daylight_surface_eye_is_discouraged_and_clamps_lifetime()
    {
        var input = Input() with { DayTime = true, PositionY = 100f, WorldSurfacePixels = 1000d, TimeLeft = 750 };
        Assert.True(VanillaFlyingEyeLifecycle.TryStep(VanillaNpcIds.DemonEye, in input, out var result));
        Assert.True(result.Discouraged);
        Assert.Equal(10, result.TimeLeft);
    }

    [Fact]
    public void Graveyard_target_suppresses_daylight_discouragement()
    {
        var input = Input() with { DayTime = true, PositionY = 100f, WorldSurfacePixels = 1000d, TargetInGraveyard = true };
        Assert.True(VanillaFlyingEyeLifecycle.TryStep(VanillaNpcIds.DemonEye, in input, out var result));
        Assert.False(result.Discouraged);
        Assert.Equal(750, result.TimeLeft);
    }

    [Fact]
    public void Below_world_surface_suppresses_daylight_discouragement()
    {
        var input = Input() with { DayTime = true, PositionY = 1001f, WorldSurfacePixels = 1000d };
        Assert.True(VanillaFlyingEyeLifecycle.TryStep(VanillaNpcIds.DemonEye, in input, out var result));
        Assert.False(result.Discouraged);
    }

    [Fact]
    public void Non_daylight_family_never_uses_discouraged_branch()
    {
        var input = Input() with { DayTime = true, PositionY = 100f, WorldSurfacePixels = 1000d };
        Assert.True(VanillaFlyingEyeLifecycle.TryStep(VanillaNpcIds.PigronCorruption, in input, out var result));
        Assert.False(result.Discouraged);
    }

    [Fact]
    public void Pigron_enters_phasing_after_exactly_three_hundred_blocked_ticks()
    {
        var input = Input() with
        {
            Ai = new NpcAiState(299f, 0f, 0f, 0f),
            HasLineOfSight = false,
            NoTileCollide = false
        };
        Assert.True(VanillaFlyingEyeLifecycle.TryStep(VanillaNpcIds.PigronCorruption, in input, out var result));
        Assert.Equal(0f, result.Ai.Ai0);
        Assert.Equal(1f, result.Ai.Ai1);
        Assert.True(result.NoTileCollide);
    }

    [Fact]
    public void Pigron_stays_phased_while_line_of_sight_returns_inside_solid_tiles()
    {
        var input = Input() with
        {
            Ai = new NpcAiState(0f, 1f, 0f, 0f),
            HasLineOfSight = true,
            SolidCollision = true,
            NoTileCollide = true
        };
        Assert.True(VanillaFlyingEyeLifecycle.TryStep(VanillaNpcIds.PigronHallow, in input, out var result));
        Assert.Equal(1f, result.Ai.Ai1);
        Assert.True(result.NoTileCollide);
    }

    [Fact]
    public void Pigron_leaves_phasing_only_after_los_returns_in_clear_space()
    {
        var input = Input() with
        {
            Ai = new NpcAiState(88f, 1f, 0f, 0f),
            HasLineOfSight = true,
            SolidCollision = false,
            NoTileCollide = true
        };
        Assert.True(VanillaFlyingEyeLifecycle.TryStep(VanillaNpcIds.PigronCrimson, in input, out var result));
        Assert.Equal(0f, result.Ai.Ai0);
        Assert.Equal(0f, result.Ai.Ai1);
        Assert.False(result.NoTileCollide);
    }

    [Fact]
    public void Invalid_state_fails_closed()
    {
        var input = Input() with { PositionY = float.NaN };
        Assert.False(VanillaFlyingEyeLifecycle.TryStep(VanillaNpcIds.DemonEye, in input, out _));
    }

    [Fact]
    public void Integration_preserves_pre_phase_collision_flag_then_commits_no_clip()
    {
        var inner = new CapturingStepper();
        var stepper = new VanillaNpcTargetingAiStepper(inner);
        stepper.SetFlyingEyeEnvironment(new FakeEnvironment(false, false, false));
        stepper.SetCandidates([Candidate(0, 500f, 100f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);

        NpcSnapshot npc = Snapshot(VanillaNpcIds.PigronCorruption, ai: new NpcAiState(299f, 0f, 0f, 0f));
        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Assert.False(inner.Last.Simulation.NoTileCollide);
        Assert.True(next.Simulation.NoTileCollide);
        Assert.Equal(1f, next.Ai.Ai1);
    }

    [Fact]
    public void Integration_daylight_branch_keeps_current_target_and_forces_upward_direction()
    {
        var inner = new CapturingStepper();
        var stepper = new VanillaNpcTargetingAiStepper(inner);
        stepper.EnableBlueSlimeMotion(100d);
        stepper.SetFlyingEyeEnvironment(new FakeEnvironment(true, false, false));
        stepper.SetCandidates([Candidate(0, 500f, 500f), Candidate(1, 101f, 101f)]);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);

        NpcSnapshot npc = Snapshot(VanillaNpcIds.DemonEye, target: 0, velocityY: 1f);
        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Assert.Equal((ushort)0, inner.Last.Target);
        Assert.Equal(1, inner.Last.Simulation.DirectionX);
        Assert.Equal(-1, inner.Last.Simulation.DirectionY);
        Assert.Equal(10, next.Simulation.TimeLeft);
    }

    private static VanillaFlyingEyeLifecycleInput Input() => new(
        PositionY: 0f,
        VelocityY: 0f,
        Ai: default,
        TimeLeft: 750,
        NoTileCollide: false,
        DayTime: false,
        WorldSurfacePixels: double.PositiveInfinity,
        TargetInGraveyard: false,
        HasLineOfSight: true,
        SolidCollision: false);

    private static VanillaNpcTargetCandidate Candidate(byte slot, float x, float y) =>
        new(slot, x, y, 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private static NpcSnapshot Snapshot(
        NpcTypeId type,
        ushort target = 0,
        float velocityY = 0f,
        NpcAiState ai = default)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        return new NpcSnapshot(
            new NpcHandle(1, new NpcGeneration(1)),
            new NpcRevision(1),
            type.Value,
            checked((short)type.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: velocityY,
            Target: target,
            Ai: ai,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = -1,
                DirectionY = 1,
                Scale = definition.Scale,
                Life = definition.LifeMax,
                LifeMax = definition.LifeMax,
                TimeLeft = 750
            });
    }

    private sealed class FakeEnvironment(bool hasLineOfSight, bool solidCollision, bool graveyard) : IVanillaFlyingEyeEnvironment
    {
        public bool IsGraveyardAt(float centerX, float centerY) => graveyard;
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight, float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => hasLineOfSight;
        public bool SolidCollision(float positionX, float positionY, int width, int height) => solidCollision;
    }

    private sealed class CapturingStepper : INpcAiStateStepper
    {
        public NpcSnapshot Last { get; private set; }

        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            Last = npc;
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }
    }
}
