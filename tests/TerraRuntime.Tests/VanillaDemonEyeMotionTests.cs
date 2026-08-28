using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaDemonEyeMotionTests
{
    [Fact]
    public void Ordinary_air_motion_uses_verified_style2_acceleration()
    {
        VanillaDemonEyeMotionInput input = CreateInput(
            velocityX: 1f,
            velocityY: 1f,
            directionX: -1,
            directionY: -1);

        Assert.True(VanillaDemonEyeMotion.TryStep(in input, out VanillaDemonEyeMotionResult result));

        Assert.Equal(0.95f, result.VelocityX, precision: 5);
        Assert.Equal(0.99f, result.VelocityY, precision: 5);
        Assert.True(result.NoGravity);
    }

    [Fact]
    public void Tile_collision_reflects_old_velocity_before_steering()
    {
        VanillaDemonEyeMotionInput input = CreateInput(
            velocityX: 8f,
            velocityY: 8f,
            oldVelocityX: 1f,
            oldVelocityY: -0.5f,
            directionX: 1,
            directionY: -1,
            collideX: true,
            collideY: true);

        Assert.True(VanillaDemonEyeMotion.TryStep(in input, out VanillaDemonEyeMotionResult result));

        Assert.Equal(-1.95f, result.VelocityX, precision: 5);
        Assert.Equal(0.99f, result.VelocityY, precision: 5);
    }

    [Fact]
    public void No_tile_collide_skips_reflection()
    {
        VanillaDemonEyeMotionInput input = CreateInput(
            velocityX: 1f,
            velocityY: 0f,
            oldVelocityX: 10f,
            directionX: 1,
            directionY: 0,
            noTileCollide: true,
            collideX: true);

        Assert.True(VanillaDemonEyeMotion.TryStep(in input, out VanillaDemonEyeMotionResult result));

        Assert.Equal(1.1f, result.VelocityX, precision: 5);
        Assert.Equal(0f, result.VelocityY);
    }

    [Fact]
    public void Scale_changes_the_verified_speed_caps()
    {
        VanillaDemonEyeMotionInput input = CreateInput(
            velocityX: 5.95f,
            velocityY: 2.2f,
            directionX: 1,
            directionY: 1,
            scale: 0.5f);

        Assert.True(VanillaDemonEyeMotion.TryStep(in input, out VanillaDemonEyeMotionResult result));

        Assert.Equal(6f, result.VelocityX, precision: 5);
        Assert.Equal(2.24f, result.VelocityY, precision: 5);
    }

    [Fact]
    public void Wet_motion_applies_final_upward_drift_and_cap()
    {
        VanillaDemonEyeMotionInput input = CreateInput(
            velocityX: 0f,
            velocityY: -3.8f,
            directionX: 0,
            directionY: 0,
            wet: true);

        Assert.True(VanillaDemonEyeMotion.TryStep(in input, out VanillaDemonEyeMotionResult result));

        Assert.Equal(-4f, result.VelocityY);
    }

    [Fact]
    public void Invalid_numeric_or_direction_input_is_rejected()
    {
        VanillaDemonEyeMotionInput badScale = CreateInput(scale: float.NaN);
        VanillaDemonEyeMotionInput badDirection = CreateInput(directionX: 2);

        Assert.False(VanillaDemonEyeMotion.TryStep(in badScale, out _));
        Assert.False(VanillaDemonEyeMotion.TryStep(in badDirection, out _));
    }

    private static VanillaDemonEyeMotionInput CreateInput(
        float velocityX = 0f,
        float velocityY = 0f,
        float oldVelocityX = 0f,
        float oldVelocityY = 0f,
        int directionX = 0,
        int directionY = 0,
        float scale = 1f,
        bool noTileCollide = false,
        bool collideX = false,
        bool collideY = false,
        bool wet = false) =>
        new(
            VelocityX: velocityX,
            VelocityY: velocityY,
            OldVelocityX: oldVelocityX,
            OldVelocityY: oldVelocityY,
            DirectionX: directionX,
            DirectionY: directionY,
            Scale: scale,
            NoTileCollide: noTileCollide,
            CollideX: collideX,
            CollideY: collideY,
            Wet: wet);
}
