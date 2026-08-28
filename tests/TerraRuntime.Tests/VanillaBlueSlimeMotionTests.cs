using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaBlueSlimeMotionTests
{
    [Fact]
    public void First_tick_initializes_ai2_and_refreshes_target_before_ground_idle()
    {
        VanillaBlueSlimeMotionResult result = Step(
            positionX: 160f,
            velocityX: 0f,
            velocityY: 0f,
            directionX: 0,
            target: byte.MaxValue,
            ai: new NpcAiState(12f, 0f, 0f, 0f),
            closestTarget: new VanillaBlueSlimeTargetRefresh(true, 4, -1, 1));

        Assert.Equal(4, result.Target);
        Assert.Equal(-1, result.DirectionX);
        Assert.Equal(1, result.DirectionY);
        Assert.Equal(-99f, result.Ai.Ai0);
        Assert.Equal(1f, result.Ai.Ai2);
        Assert.Equal(1, result.TargetRefreshes);
        Assert.Equal(0f, result.VelocityX);
        Assert.Equal(0f, result.VelocityY);
    }

    [Fact]
    public void Ground_timer_uses_vanilla_normal_jump_band()
    {
        VanillaBlueSlimeMotionResult result = Step(
            velocityX: 1f,
            velocityY: 0f,
            directionX: 1,
            target: 2,
            ai: new NpcAiState(0f, 0f, 1f, 0f));

        Assert.Equal(2.8f, result.VelocityX, 3);
        Assert.Equal(-6f, result.VelocityY);
        Assert.Equal(-1120f, result.Ai.Ai0);
        Assert.Equal(0f, result.Ai.Ai3);
    }

    [Fact]
    public void Deep_timer_band_performs_large_jump_and_remembers_x()
    {
        VanillaBlueSlimeMotionResult result = Step(
            positionX: 192f,
            velocityX: 0f,
            velocityY: 0f,
            directionX: -1,
            target: 2,
            ai: new NpcAiState(-1600f, 0f, 1f, 0f));

        Assert.Equal(-3f, result.VelocityX);
        Assert.Equal(-8f, result.VelocityY);
        Assert.Equal(-200f, result.Ai.Ai0);
        Assert.Equal(192f, result.Ai.Ai3);
    }

    [Fact]
    public void Engaged_ground_tick_advances_jump_timer_twice_and_retargets_on_jump()
    {
        VanillaBlueSlimeMotionResult result = Step(
            velocityX: 0f,
            velocityY: 0f,
            directionX: 1,
            target: 1,
            ai: new NpcAiState(-1f, 0f, 1f, 0f),
            engaged: true,
            closestTarget: new VanillaBlueSlimeTargetRefresh(true, 7, -1, -1));

        Assert.Equal(7, result.Target);
        Assert.Equal(-1, result.DirectionX);
        Assert.Equal(-2f, result.VelocityX);
        Assert.Equal(-6f, result.VelocityY);
        Assert.Equal(-1120f, result.Ai.Ai0);
        Assert.Equal(1, result.TargetRefreshes);
    }

    [Fact]
    public void Wet_collision_bounces_up_and_applies_water_escape_acceleration()
    {
        VanillaBlueSlimeMotionResult result = Step(
            velocityX: 0.5f,
            velocityY: 1f,
            oldVelocityY: 1f,
            directionX: 1,
            target: 3,
            ai: new NpcAiState(-200f, 0f, 5f, 0f),
            wet: true,
            collideY: true);

        Assert.Equal(-2.5f, result.VelocityY);
        Assert.Equal(4f, result.Ai.Ai2);
    }

    [Fact]
    public void Airborne_targeted_slime_steers_toward_facing_direction()
    {
        VanillaBlueSlimeMotionResult result = Step(
            velocityX: 0f,
            velocityY: -3f,
            directionX: 1,
            target: 3,
            ai: new NpcAiState(-200f, 0f, 1f, 0f));

        Assert.Equal(0.2f, result.VelocityX, 3);
        Assert.Equal(-3f, result.VelocityY);
    }

    [Fact]
    public void Ground_overlap_correction_uses_old_vertical_collision_state()
    {
        VanillaBlueSlimeMotionResult result = Step(
            positionX: 100f,
            velocityX: 1.5f,
            velocityY: 0f,
            oldVelocityY: 2f,
            directionX: 1,
            target: 3,
            ai: new NpcAiState(-200f, 0f, 2f, 0f),
            collideY: true,
            solidCollision: true);

        Assert.Equal(97.5f, result.PositionX, 3);
    }

    [Fact]
    public void Contained_item_3609_uses_special_ground_acceleration_and_clamp()
    {
        VanillaBlueSlimeMotionResult result = Step(
            velocityX: 2.49f,
            velocityY: 0f,
            directionX: 1,
            target: 3,
            ai: new NpcAiState(-200f, 3609f, 2f, 0f));

        Assert.Equal(2.5f, result.VelocityX, 3);
    }

    [Fact]
    public void Invalid_target_refresh_is_rejected()
    {
        var input = new VanillaBlueSlimeMotionInput(
            PositionX: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            OldVelocityY: 0f,
            DirectionX: 1,
            DirectionY: 1,
            Target: byte.MaxValue,
            Ai: default,
            Wet: false,
            CollideX: false,
            CollideY: false,
            Engaged: false,
            SolidCollision: false,
            ClosestTarget: new VanillaBlueSlimeTargetRefresh(true, 2, 0, 1));

        Assert.False(VanillaBlueSlimeMotion.TryStep(in input, out _));
    }

    private static VanillaBlueSlimeMotionResult Step(
        float positionX = 160f,
        float velocityX = 0f,
        float velocityY = 0f,
        float oldVelocityY = 0f,
        int directionX = 1,
        int directionY = 1,
        ushort target = byte.MaxValue,
        NpcAiState ai = default,
        bool wet = false,
        bool collideX = false,
        bool collideY = false,
        bool engaged = false,
        bool solidCollision = false,
        VanillaBlueSlimeTargetRefresh closestTarget = default)
    {
        var input = new VanillaBlueSlimeMotionInput(
            positionX,
            velocityX,
            velocityY,
            oldVelocityY,
            directionX,
            directionY,
            target,
            ai,
            wet,
            collideX,
            collideY,
            engaged,
            solidCollision,
            closestTarget);

        Assert.True(VanillaBlueSlimeMotion.TryStep(in input, out VanillaBlueSlimeMotionResult result));
        return result;
    }
}
