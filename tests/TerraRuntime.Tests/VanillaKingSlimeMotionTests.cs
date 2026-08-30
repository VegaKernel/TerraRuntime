using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaKingSlimeMotionTests
{
    [Fact]
    public void First_server_tick_initializes_source_slots_and_preserves_full_health_scale()
    {
        VanillaKingSlimeMotionInput input = CreateInput();
        Assert.True(VanillaKingSlimeMotion.TryStep(in input, out VanillaKingSlimeMotionResult result));
        Assert.Equal((ushort)7, result.Target);
        Assert.Equal(1, result.DirectionX);
        Assert.Equal(-98f, result.Ai.Ai0);
        Assert.Equal(2000f, result.Ai.Ai3);
        Assert.Equal(1f, result.LocalAi.Initialized);
        Assert.Equal(1.25f, result.Scale, 5);
        Assert.False(result.Hidden);
        Assert.False(result.MinionBurstRequested);
    }

    [Theory]
    [InlineData(0f, -8f, 4f, 1f, -120f)]
    [InlineData(1f, -8f, 4f, 2f, -120f)]
    [InlineData(2f, -6f, 4.5f, 3f, -120f)]
    [InlineData(3f, -13f, 3.5f, 0f, -200f)]
    public void Grounded_jump_cycle_matches_verified_ai15_states(float ai1, float expectedVelocityY, float expectedVelocityX, float expectedAi1, float expectedAi0)
    {
        VanillaKingSlimeMotionInput input = CreateInput() with
        {
            Ai = new NpcAiState(0f, ai1, 0f, 2000f),
            LocalAi = InitializedLocalAi()
        };
        Assert.True(VanillaKingSlimeMotion.TryStep(in input, out VanillaKingSlimeMotionResult result));
        Assert.Equal(expectedVelocityY, result.VelocityY, 5);
        Assert.Equal(expectedVelocityX, result.VelocityX, 5);
        Assert.Equal(expectedAi1, result.Ai.Ai1);
        Assert.Equal(expectedAi0, result.Ai.Ai0);
    }

    [Fact]
    public void Low_life_accelerates_ground_jump_timer_by_verified_thresholds()
    {
        VanillaKingSlimeMotionInput input = CreateInput() with
        {
            Life = 100,
            Ai = new NpcAiState(-12f, 0f, 0f, 2000f),
            LocalAi = InitializedLocalAi()
        };
        Assert.True(VanillaKingSlimeMotion.TryStep(in input, out VanillaKingSlimeMotionResult result));
        Assert.Equal(-8f, result.VelocityY, 5);
        Assert.Equal(-120f, result.Ai.Ai0);
        Assert.Equal(1f, result.Ai.Ai1);
    }

    [Fact]
    public void Teleport_trigger_fails_closed_until_world_resolver_supplies_destination()
    {
        VanillaKingSlimeMotionInput input = CreateInput() with
        {
            Ai = new NpcAiState(-100f, 0f, 300f, 2000f),
            LocalAi = InitializedLocalAi()
        };
        Assert.True(VanillaKingSlimeMotion.RequiresTeleportDestination(in input, out bool antiCheese));
        Assert.False(antiCheese);
        Assert.False(VanillaKingSlimeMotion.TryStep(in input, out _));
    }

    [Fact]
    public void Teleport_request_marks_anti_cheese_after_verified_pressure_or_distance_limit()
    {
        VanillaKingSlimeMotionInput pressure = CreateInput() with
        {
            Ai = new NpcAiState(-100f, 0f, 300f, 2000f),
            LocalAi = new VanillaKingSlimeLocalAi(360f, 0f, 0f, 1f)
        };
        VanillaKingSlimeMotionInput distance = pressure with
        {
            LocalAi = InitializedLocalAi(),
            TargetCandidate = Candidate(7, 2501f, 100f),
            ClosestCandidate = Candidate(7, 2501f, 100f)
        };
        Assert.True(VanillaKingSlimeMotion.RequiresTeleportDestination(in pressure, out bool pressureAntiCheese));
        Assert.True(pressureAntiCheese);
        Assert.True(VanillaKingSlimeMotion.RequiresTeleportDestination(in distance, out bool distanceAntiCheese));
        Assert.True(distanceAntiCheese);
    }

    [Fact]
    public void Shrink_completion_moves_bottom_to_resolved_spot_then_enters_grow_state()
    {
        VanillaKingSlimeMotionInput input = CreateInput() with
        {
            PositionX = 100f,
            PositionY = 100f,
            Ai = new NpcAiState(59f, 5f, 0f, 2000f),
            LocalAi = new VanillaKingSlimeLocalAi(0f, 500f, 600f, 1f)
        };
        Assert.True(VanillaKingSlimeMotion.TryStep(in input, out VanillaKingSlimeMotionResult result));
        Assert.Equal(6f, result.Ai.Ai1);
        Assert.Equal(0f, result.Ai.Ai0);
        Assert.True(result.Hidden);
        Assert.True(result.DontTakeDamage);
        Assert.Equal(0.625f, result.Scale, 5);
        Assert.Equal(61, (int)MathF.Floor(98f * result.Scale));
        Assert.Equal(57, (int)MathF.Floor(92f * result.Scale));
        Assert.Equal(600f, result.PositionY + 57f, 5);
        Assert.InRange(result.PositionX + 61f * 0.5f, 499.49f, 500.51f);
    }

    [Fact]
    public void Grow_completion_restores_full_scale_and_normal_state()
    {
        VanillaKingSlimeMotionInput input = CreateInput() with
        {
            Scale = 0.625f,
            Ai = new NpcAiState(29f, 6f, 0f, 2000f),
            LocalAi = InitializedLocalAi()
        };
        Assert.True(VanillaKingSlimeMotion.TryStep(in input, out VanillaKingSlimeMotionResult result));
        Assert.Equal(0f, result.Ai.Ai1);
        Assert.Equal(0f, result.Ai.Ai0);
        Assert.Equal(1.25f, result.Scale, 5);
        Assert.False(result.Hidden);
        Assert.False(result.DontTakeDamage);
    }

    [Fact]
    public void Normal_scale_tracks_life_and_good_world_multiplier()
    {
        VanillaKingSlimeMotionInput normal = CreateInput() with
        {
            Life = 1000,
            Ai = new NpcAiState(-100f, 0f, 0f, 2000f),
            LocalAi = InitializedLocalAi()
        };
        VanillaKingSlimeMotionInput goodWorld = normal with { GoodWorld = true };
        Assert.True(VanillaKingSlimeMotion.TryStep(in normal, out VanillaKingSlimeMotionResult normalResult));
        Assert.True(VanillaKingSlimeMotion.TryStep(in goodWorld, out VanillaKingSlimeMotionResult goodWorldResult));
        Assert.Equal(1f, normalResult.Scale, 5);
        Assert.Equal(1.5f, goodWorldResult.Scale, 5);
    }

    [Fact]
    public void Life_drop_crossing_five_percent_requests_one_external_minion_burst()
    {
        VanillaKingSlimeMotionInput crossed = CreateInput() with
        {
            Life = 1899,
            Ai = new NpcAiState(-100f, 0f, 0f, 2000f),
            LocalAi = InitializedLocalAi()
        };
        VanillaKingSlimeMotionInput boundary = crossed with { Life = 1900 };
        Assert.True(VanillaKingSlimeMotion.TryStep(in crossed, out VanillaKingSlimeMotionResult crossedResult));
        Assert.True(crossedResult.MinionBurstRequested);
        Assert.Equal(1899f, crossedResult.Ai.Ai3);
        Assert.True(VanillaKingSlimeMotion.TryStep(in boundary, out VanillaKingSlimeMotionResult boundaryResult));
        Assert.False(boundaryResult.MinionBurstRequested);
        Assert.Equal(2000f, boundaryResult.Ai.Ai3);
    }

    [Fact]
    public void Blocked_line_of_sight_advances_teleport_pressure_and_timer()
    {
        VanillaKingSlimeMotionInput input = CreateInput() with
        {
            VelocityY = -1f,
            CanHitTarget = false,
            Ai = new NpcAiState(-100f, 0f, 10f, 2000f),
            LocalAi = new VanillaKingSlimeLocalAi(12f, 0f, 0f, 1f)
        };
        Assert.True(VanillaKingSlimeMotion.TryStep(in input, out VanillaKingSlimeMotionResult result));
        Assert.Equal(11f, result.Ai.Ai2);
        Assert.Equal(13f, result.LocalAi.TeleportPressure);
    }

    private static VanillaKingSlimeMotionInput CreateInput() =>
        new(
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            DirectionX: 1,
            Target: 7,
            Ai: default,
            LocalAi: default,
            Life: 2000,
            LifeMax: 2000,
            TimeLeft: 750,
            Scale: 1.25f,
            GoodWorld: false,
            CanHitTarget: true,
            TargetCandidate: Candidate(7, 300f, 100f),
            ClosestCandidate: Candidate(7, 300f, 100f),
            HasTeleportDestination: false,
            TeleportBottomX: 0f,
            TeleportBottomY: 0f,
            WorldPixelWidth: 16000f,
            WorldPixelHeight: 8000f);

    private static VanillaKingSlimeLocalAi InitializedLocalAi() => new(0f, 0f, 0f, 1f);

    private static VanillaNpcTargetCandidate Candidate(byte slot, float centerX, float centerY) =>
        new(slot, centerX, centerY, 0, Active: true, Dead: false, Ghost: false, NoAggro: false);
}
