using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEyeOfCthulhuMotionTests
{
    [Fact]
    public void Daytime_eye_rises_and_encourages_fast_despawn()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            VelocityY = 1f,
            TimeLeft = 750,
            DayTime = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(0.96f, result.VelocityY, 5);
        Assert.Equal(10, result.TimeLeft);
        Assert.Equal(input.Ai, result.Ai);
    }

    [Fact]
    public void First_phase_hover_uses_verified_classic_speed_and_acceleration()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 200f,
            TargetCenterY = 300f
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(0.04f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
        Assert.Equal(1f, result.Ai.Ai2, 5);
    }

    [Fact]
    public void Phase_one_servant_cadence_resets_at_verified_tick_threshold()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 150f,
            NpcCenterY = 155f,
            NpcBottomY = 210f,
            TargetCenterX = 250f,
            TargetCenterY = 300f,
            TargetTopY = 279f,
            Ai = new NpcAiState(0f, 0f, 42f, 109f)
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(43f, result.Ai.Ai2, 5);
        Assert.Equal(0f, result.Ai.Ai3, 5);
    }

    [Fact]
    public void Phase_one_servant_cadence_does_not_advance_when_eye_is_not_above_player()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 150f,
            NpcCenterY = 155f,
            NpcBottomY = 310f,
            TargetCenterX = 250f,
            TargetCenterY = 300f,
            TargetTopY = 279f,
            Ai = new NpcAiState(0f, 0f, 42f, 109f)
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(109f, result.Ai.Ai3, 5);
    }

    [Fact]
    public void First_phase_direct_dash_uses_six_pixel_speed()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 200f,
            TargetCenterY = 100f,
            Ai = new NpcAiState(0f, 1f, 0f, 0f)
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(6f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
        Assert.Equal(2f, result.Ai.Ai1, 5);
    }

    [Fact]
    public void Falling_below_half_life_enters_transformation()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with { Life = 1399 };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(1f, result.Ai.Ai0, 5);
        Assert.Equal(0f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
        Assert.Equal(0f, result.Ai.Ai3, 5);
    }

    [Fact]
    public void Second_transformation_stage_enters_phase_two_at_tick_one_hundred()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            VelocityX = 2f,
            VelocityY = -2f,
            Ai = new NpcAiState(2f, 99f, 0.25f, 0f)
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(3f, result.Ai.Ai0, 5);
        Assert.Equal(0f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
        Assert.Equal(1.96f, result.VelocityX, 5);
        Assert.Equal(-1.96f, result.VelocityY, 5);
    }

    [Fact]
    public void Second_phase_direct_dash_uses_six_point_eight_pixel_speed()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 100f,
            TargetCenterY = 200f,
            Ai = new NpcAiState(3f, 1f, 0f, 0f)
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(0f, result.VelocityX, 5);
        Assert.Equal(6.8f, result.VelocityY, 5);
        Assert.Equal(2f, result.Ai.Ai1, 5);
    }

    [Fact]
    public void Expert_only_rapid_dash_state_is_not_silently_approximated()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            Ai = new NpcAiState(3f, 3f, 0f, 0f)
        };

        Assert.False(VanillaEyeOfCthulhuMotion.TryStep(in input, out _));
    }

    private static VanillaEyeOfCthulhuMotionInput CreateInput() =>
        new(
            NpcCenterX: 100f,
            NpcCenterY: 100f,
            NpcBottomY: 400f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 7,
            Ai: default,
            Life: 2800,
            LifeMax: 2800,
            TimeLeft: VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            DayTime: false,
            TargetAvailable: true,
            TargetDead: false,
            TargetCenterX: 200f,
            TargetCenterY: 100f,
            TargetTopY: 0f);
}
