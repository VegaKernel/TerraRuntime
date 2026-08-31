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
    public void Expert_phase_one_hover_uses_verified_speed_acceleration_and_duration()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 200f,
            TargetCenterY = 300f,
            Ai = new NpcAiState(0f, 0f, 209f, 0f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(0.15f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
        Assert.Equal(1f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, result.Target);
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
    public void Expert_phase_one_servant_cadence_advances_at_any_vertical_offset_and_resets_at_forty_four()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 150f,
            NpcCenterY = 155f,
            NpcBottomY = 310f,
            TargetCenterX = 250f,
            TargetCenterY = 300f,
            TargetTopY = 279f,
            Ai = new NpcAiState(0f, 0f, 42f, 43f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(0f, result.Ai.Ai3, 5);
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
    public void Expert_phase_one_dash_uses_verified_speed_slowdown_and_duration()
    {
        VanillaEyeOfCthulhuMotionInput launch = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 200f,
            TargetCenterY = 100f,
            Ai = new NpcAiState(0f, 1f, 0f, 0f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in launch, out VanillaEyeOfCthulhuMotionResult launched));
        Assert.Equal(7f, launched.VelocityX, 5);
        Assert.Equal(2f, launched.Ai.Ai1, 5);

        VanillaEyeOfCthulhuMotionInput finish = CreateInput() with
        {
            VelocityX = 10f,
            Ai = new NpcAiState(0f, 2f, 99f, 2f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in finish, out VanillaEyeOfCthulhuMotionResult finished));
        Assert.Equal(10f * 0.98f * 0.985f, finished.VelocityX, 5);
        Assert.Equal(0f, finished.Ai.Ai1, 5);
        Assert.Equal(0f, finished.Ai.Ai2, 5);
        Assert.Equal(0f, finished.Ai.Ai3, 5);
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
    public void Expert_phase_one_enters_transformation_below_sixty_five_percent_life()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            Life = 1819,
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(1f, result.Ai.Ai0, 5);
        Assert.Equal(0f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
        Assert.Equal(0f, result.Ai.Ai3, 5);
    }

    [Fact]
    public void Expert_transformation_advances_spin_timer_and_velocity_like_source()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            VelocityX = 10f,
            VelocityY = -5f,
            Ai = new NpcAiState(1f, 19f, 0.1f, 0f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(1f, result.Ai.Ai0, 5);
        Assert.Equal(20f, result.Ai.Ai1, 5);
        Assert.Equal(0.105f, result.Ai.Ai2, 5);
        Assert.Equal(9.8f, result.VelocityX, 5);
        Assert.Equal(-4.9f, result.VelocityY, 5);
    }

    [Fact]
    public void Get_good_world_motion_is_admitted_by_the_source_backed_state_machine()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            ExpertMode = true,
            GoodWorld = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));
        Assert.Equal(1f, result.Ai.Ai2, 5);
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
    public void Expert_second_transformation_stage_enters_phase_two_at_tick_one_hundred()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            VelocityX = 2f,
            VelocityY = -2f,
            Ai = new NpcAiState(2f, 99f, 0.25f, 0f),
            ExpertMode = true
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
    public void Expert_phase_two_long_range_hover_uses_source_distance_bands()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 0f,
            NpcCenterY = 0f,
            TargetCenterX = 1000f,
            TargetCenterY = 120f,
            Ai = new NpcAiState(3f, 0f, 0f, 0f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(0.22f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
        Assert.Equal(1f, result.Ai.Ai2, 5);
    }

    [Fact]
    public void Expert_phase_two_third_direct_dash_uses_one_point_three_speed_multiplier()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 200f,
            TargetCenterY = 100f,
            Ai = new NpcAiState(3f, 1f, 0f, 2f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(6.8f * 1.3f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
        Assert.Equal(2f, result.Ai.Ai1, 5);
    }

    [Fact]
    public void Expert_phase_two_dash_uses_fifty_tick_slowdown_boundary()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            VelocityX = 10f,
            Ai = new NpcAiState(3f, 2f, 49f, 0f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(10f * 0.97f * 0.98f, result.VelocityX, 5);
        Assert.Equal(50f, result.Ai.Ai2, 5);
    }

    [Fact]
    public void Expert_low_life_state_five_moves_toward_six_hundred_pixel_below_target()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            NpcCenterX = 100f,
            NpcCenterY = 100f,
            TargetCenterX = 100f,
            TargetCenterY = 100f,
            Life = 300,
            Ai = new NpcAiState(3f, 0f, 0f, 0f),
            ExpertMode = true
        };

        Assert.True(VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result));

        Assert.Equal(5f, result.Ai.Ai1, 5);
        Assert.Equal(1f, result.Ai.Ai2, 5);
        Assert.Equal(0f, result.VelocityX, 5);
        Assert.Equal(0.3f, result.VelocityY, 5);
    }

    [Fact]
    public void Expert_random_rapid_dash_entry_remains_fail_closed_at_exact_rng_boundary()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            Life = 1300,
            VelocityX = 10f,
            Ai = new NpcAiState(3f, 2f, 89f, 2f),
            ExpertMode = true
        };

        Assert.False(VanillaEyeOfCthulhuMotion.TryStep(in input, out _));
    }

    [Fact]
    public void Expert_only_rapid_dash_state_is_not_silently_approximated()
    {
        VanillaEyeOfCthulhuMotionInput input = CreateInput() with
        {
            Ai = new NpcAiState(3f, 3f, 0f, 0f),
            ExpertMode = true
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
