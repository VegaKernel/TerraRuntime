using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaFlyerAiMotionTests
{
    [Fact]
    public void Eater_expert_acceleration_and_source_jitter_are_applied_in_order()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.EaterOfSouls);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.EaterOfSouls) with
        {
            TargetCenterX = 400f,
            ExpertMode = true,
            Ai = new NpcAiState(0f, 0f, 0f, 0f)
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.EaterOfSouls, in input, in profile, out var result));

        Assert.Equal(1f, result.Ai.Ai0);
        Assert.Equal(0.012f, result.VelocityX, 5);
        Assert.Equal(-0.012f, result.VelocityY, 5);
    }

    [Fact]
    public void Eater_close_homing_adds_source_point_zero_zero_seven_seek_component()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.EaterOfSouls);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.EaterOfSouls) with
        {
            TargetCenterX = 96f
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.EaterOfSouls, in input, in profile, out var result));

        Assert.Equal(0f, result.Ai.Ai0);
        Assert.Equal(0.048f, result.VelocityX, 5);
    }

    [Fact]
    public void Bee_ramp_advances_ai1_and_reaches_verified_speed_and_acceleration_band()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.Bee);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.Bee) with
        {
            TargetCenterX = 400f,
            Ai = new NpcAiState(0f, 119f, 0f, 0f)
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.Bee, in input, in profile, out var result));

        Assert.Equal(120f, result.Ai.Ai1);
        Assert.Equal(1f, result.Ai.Ai0);
        Assert.Equal(0.077f, result.VelocityX, 5);
    }

    [Fact]
    public void Hornet_above_surface_damps_upward_escape_when_target_is_far_below()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.Hornet);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.Hornet) with
        {
            PositionY = 100f,
            NpcCenterY = 116f,
            TargetCenterY = 521f,
            TargetTopY = 500f,
            VelocityY = -2f,
            WorldSurfacePixels = 1000d
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.Hornet, in input, in profile, out var result));

        Assert.True(result.VelocityY > -2f);
        Assert.Equal(1f, result.Ai.Ai0);
    }

    [Fact]
    public void Collision_bounce_preserves_source_minimum_horizontal_rebound()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.EaterOfSouls);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.EaterOfSouls) with
        {
            CollideX = true,
            OldVelocityX = -1f,
            DirectionX = -1,
            TargetCenterX = 400f
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.EaterOfSouls, in input, in profile, out var result));

        Assert.Equal(2f, result.VelocityX);
    }

    [Fact]
    public void Collision_bounce_preserves_source_minimum_vertical_rebound()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.Corruptor);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.Corruptor) with
        {
            CollideY = true,
            OldVelocityY = -1f,
            TargetCenterX = 400f
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.Corruptor, in input, in profile, out var result));

        Assert.Equal(2f, result.VelocityY);
    }

    [Fact]
    public void Probe_daylight_flight_clamps_lifetime_to_ten_ticks()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.Probe);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.Probe) with
        {
            DayTime = true,
            TimeLeft = 750,
            TargetCenterX = 400f
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.Probe, in input, in profile, out var result));

        Assert.Equal(10, result.TimeLeft);
        Assert.True(result.VelocityY < 0f);
    }

    [Fact]
    public void Blood_squid_uses_its_dedicated_daylight_despawn_window()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.BloodSquid);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.BloodSquid) with
        {
            DayTime = true,
            TimeLeft = 750,
            TargetCenterX = 400f,
            TargetCenterY = 0f
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.BloodSquid, in input, in profile, out var result));

        Assert.Equal(60, result.TimeLeft);
        Assert.True(result.VelocityY < -0.4f);
    }

    [Fact]
    public void Servant_uses_generic_daylight_escape_and_ten_tick_despawn()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.ServantOfCthulhu);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.ServantOfCthulhu) with
        {
            DayTime = true,
            TimeLeft = 750,
            TargetCenterX = 400f
        };

        Assert.True(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.ServantOfCthulhu, in input, in profile, out var result));

        Assert.Equal(10, result.TimeLeft);
        Assert.Equal(-0.06f, result.VelocityY, 5);
    }

    [Fact]
    public void Non_finite_input_fails_closed()
    {
        VanillaFlyerMotionProfile profile = Profile(VanillaNpcIds.EaterOfSouls);
        VanillaFlyerAiMotionInput input = Input(VanillaNpcIds.EaterOfSouls) with
        {
            NpcCenterX = float.NaN
        };

        Assert.False(VanillaFlyerAiMotion.TryStep(VanillaNpcIds.EaterOfSouls, in input, in profile, out _));
    }

    private static VanillaFlyerMotionProfile Profile(NpcTypeId type)
    {
        Assert.True(VanillaFlyerNpcCatalog.TryGetMotionProfile(type, out VanillaFlyerMotionProfile profile));
        return profile;
    }

    private static VanillaFlyerAiMotionInput Input(NpcTypeId type) => new(
        PositionY: 0f,
        NpcCenterX: 0f,
        NpcCenterY: 0f,
        VelocityX: 0f,
        VelocityY: 0f,
        TargetCenterX: 0f,
        TargetCenterY: 0f,
        TargetTopY: -21f,
        OldVelocityX: 0f,
        OldVelocityY: 0f,
        DirectionX: 1,
        Ai: default,
        Scale: 1f,
        CollideX: false,
        CollideY: false,
        Wet: false,
        DayTime: false,
        ExpertMode: false,
        WorldSurfacePixels: double.PositiveInfinity,
        TimeLeft: 750);
}
