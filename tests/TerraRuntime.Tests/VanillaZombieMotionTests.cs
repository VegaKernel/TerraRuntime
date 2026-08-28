using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieMotionTests
{
    [Fact]
    public void Pursuit_refreshes_target_and_accelerates_toward_it()
    {
        VanillaZombieMotionInput input = CreateInput() with
        {
            DirectionX = -1,
            ClosestTarget = new VanillaZombieTargetRefresh(true, 7, 1, -1)
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal((ushort)7, result.Target);
        Assert.Equal(1, result.DirectionX);
        Assert.Equal(-1, result.DirectionY);
        Assert.Equal(0.07f, result.VelocityX, 5);
        Assert.Equal(1, result.TargetRefreshes);
    }

    [Fact]
    public void Unchanged_position_advances_stuck_counter()
    {
        VanillaZombieMotionInput input = CreateInput() with
        {
            PositionX = 100f,
            OldPositionX = 100f,
            Ai = new NpcAiState(0f, 0f, 0f, 10f)
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(11f, result.Ai.Ai3);
    }

    [Fact]
    public void Healthy_horizontal_progress_reduces_existing_stuck_counter()
    {
        VanillaZombieMotionInput input = CreateInput() with
        {
            PositionX = 101f,
            OldPositionX = 100f,
            VelocityX = 1f,
            Ai = new NpcAiState(0f, 0f, 0f, 10f),
            ClosestTarget = new VanillaZombieTargetRefresh(true, 4, 1, 1)
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(9f, result.Ai.Ai3);
    }

    [Fact]
    public void Stuck_threshold_suppresses_target_refresh()
    {
        VanillaZombieMotionInput input = CreateInput() with
        {
            PositionX = 100f,
            OldPositionX = 100f,
            Ai = new NpcAiState(0f, 0f, 0f, 59f),
            ClosestTarget = new VanillaZombieTargetRefresh(true, 9, -1, 1)
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(60f, result.Ai.Ai3);
        Assert.Equal(0, result.TargetRefreshes);
        Assert.NotEqual((ushort)9, result.Target);
    }

    [Fact]
    public void Overlapping_target_clears_stuck_counter_before_targeting()
    {
        VanillaZombieMotionInput input = CreateInput() with
        {
            PositionX = 100f,
            OldPositionX = 100f,
            Ai = new NpcAiState(0f, 0f, 0f, 80f),
            TargetOverlaps = true,
            ClosestTarget = new VanillaZombieTargetRefresh(true, 3, -1, -1)
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(0f, result.Ai.Ai3);
        Assert.Equal(1, result.TargetRefreshes);
        Assert.Equal((ushort)3, result.Target);
    }

    [Fact]
    public void Scale_changes_default_type_three_speed_cap()
    {
        VanillaZombieMotionInput input = CreateInput() with
        {
            VelocityX = 1.4f,
            DirectionX = 1,
            Scale = 0.5f,
            ClosestTarget = default
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(1.47f, result.VelocityX, 5);
    }

    private static VanillaZombieMotionInput CreateInput() =>
        new(
            PositionX: 100f,
            OldPositionX: 99f,
            VelocityX: 0f,
            VelocityY: 0f,
            DirectionX: 1,
            DirectionY: 1,
            Target: byte.MaxValue,
            Ai: default,
            Scale: 1f,
            TargetOverlaps: false,
            ClosestTarget: new VanillaZombieTargetRefresh(true, 2, 1, 1));
}
