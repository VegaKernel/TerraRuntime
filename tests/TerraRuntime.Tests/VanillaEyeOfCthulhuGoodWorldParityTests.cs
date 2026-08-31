using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEyeOfCthulhuGoodWorldParityTests
{
    [Fact]
    public void Expert_good_world_phase_one_hover_adds_source_speed_and_acceleration()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 300f,
            TargetCenterY = 300f,
            ExpertMode = true,
            GoodWorld = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));
        Assert.Equal(0.2f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
    }

    [Fact]
    public void Expert_good_world_phase_one_dash_uses_eight_pixel_speed()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 300f,
            TargetCenterY = 100f,
            Ai = new NpcAiState(0f, 1f, 0f, 0f),
            ExpertMode = true,
            GoodWorld = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));
        Assert.Equal(8f, result.VelocityX, 5);
        Assert.Equal(2f, result.Ai.Ai1, 5);
    }

    [Fact]
    public void Expert_good_world_phase_one_dash_uses_point_nine_nine_slowdown_and_eighty_five_ticks()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            VelocityX = 10f,
            Ai = new NpcAiState(0f, 2f, 84f, 2f),
            ExpertMode = true,
            GoodWorld = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));
        Assert.Equal(10f * 0.98f * 0.985f * 0.99f, result.VelocityX, 5);
        Assert.Equal(0f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
        Assert.Equal(0f, result.Ai.Ai3, 5);
    }

    [Fact]
    public void Good_world_transformation_still_uses_one_hundred_ticks()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            Ai = new NpcAiState(2f, 98f, 0.25f, 0f),
            GoodWorld = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));
        Assert.Equal(2f, result.Ai.Ai0, 5);
        Assert.Equal(99f, result.Ai.Ai1, 5);
    }

    [Fact]
    public void Good_world_phase_two_hover_adds_one_speed_and_point_one_acceleration()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 300f,
            TargetCenterY = 220f,
            Ai = new NpcAiState(3f, 0f, 0f, 0f),
            GoodWorld = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));
        Assert.Equal(0.17f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
    }

    [Fact]
    public void Expert_phase_two_hover_duration_tracks_source_life_bands_down_to_one_hundred_thirty()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            Life = 1399,
            Ai = new NpcAiState(3f, 0f, 129f, 0f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));
        Assert.Equal(1f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
    }

    [Fact]
    public void Expert_good_world_third_phase_two_dash_multiplies_source_speed_by_one_point_two()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 300f,
            TargetCenterY = 100f,
            Ai = new NpcAiState(3f, 1f, 0f, 2f),
            ExpertMode = true,
            GoodWorld = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));
        Assert.Equal(6.8f * 1.3f * 1.2f, result.VelocityX, 5);
        Assert.Equal(2f, result.Ai.Ai1, 5);
    }

    private static VanillaEyeOfCthulhuMotionInput CreateInput() => new(
        NpcCenterX: 100f,
        NpcCenterY: 100f,
        NpcBottomY: 155f,
        VelocityX: 0f,
        VelocityY: 0f,
        Target: 0,
        Ai: default,
        Life: 2800,
        LifeMax: 2800,
        TimeLeft: 750,
        DayTime: false,
        TargetAvailable: true,
        TargetDead: false,
        TargetCenterX: 200f,
        TargetCenterY: 300f,
        TargetTopY: 279f);
}