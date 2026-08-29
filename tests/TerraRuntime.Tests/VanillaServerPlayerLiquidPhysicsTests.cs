namespace TerraRuntime.Tests;

public sealed class VanillaServerPlayerLiquidPhysicsTests
{
    [Fact]
    public void Dry_profile_pins_1458_gravity_fall_speed_and_jump()
    {
        VanillaServerPlayerLiquidState state = VanillaServerPlayerLiquidState.Dry;

        VanillaServerPlayerMotionProfile profile =
            VanillaServerPlayerLiquidPhysics.ResolveMotionProfile(in state);

        Assert.Equal(0.4f, profile.Gravity);
        Assert.Equal(10.01f, profile.MaximumFallSpeed);
        Assert.Equal(5.01f, profile.JumpSpeed);
        Assert.Equal(15, profile.JumpHeight);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Water_and_lava_share_the_ordinary_wet_profile(bool lava)
    {
        var state = new VanillaServerPlayerLiquidState(Wet: true, Lava: lava, Honey: false, Shimmer: false);

        VanillaServerPlayerMotionProfile profile =
            VanillaServerPlayerLiquidPhysics.ResolveMotionProfile(in state);

        Assert.Equal(0.2f, profile.Gravity);
        Assert.Equal(5.01f, profile.MaximumFallSpeed);
        Assert.Equal(6.01f, profile.JumpSpeed);
        Assert.Equal(30, profile.JumpHeight);
    }

    [Fact]
    public void Honey_keeps_base_jump_but_uses_honey_gravity_and_fall_speed()
    {
        var state = new VanillaServerPlayerLiquidState(Wet: true, Lava: false, Honey: true, Shimmer: false);

        VanillaServerPlayerMotionProfile profile =
            VanillaServerPlayerLiquidPhysics.ResolveMotionProfile(in state);

        Assert.Equal(0.1f, profile.Gravity);
        Assert.Equal(3.01f, profile.MaximumFallSpeed);
        Assert.Equal(5.01f, profile.JumpSpeed);
        Assert.Equal(15, profile.JumpHeight);
    }

    [Fact]
    public void Shimmer_overrides_other_wet_profile_flags()
    {
        var state = new VanillaServerPlayerLiquidState(Wet: true, Lava: false, Honey: true, Shimmer: true);

        VanillaServerPlayerMotionProfile profile =
            VanillaServerPlayerLiquidPhysics.ResolveMotionProfile(in state);

        Assert.Equal(0.15f, profile.Gravity);
        Assert.Equal(10.01f, profile.MaximumFallSpeed);
        Assert.Equal(5.51f, profile.JumpSpeed);
        Assert.Equal(23, profile.JumpHeight);
    }

    [Theory]
    [InlineData(30, 30, 6)]
    [InlineData(7, 30, 6)]
    [InlineData(6, 30, 6)]
    [InlineData(15, 15, 3)]
    [InlineData(23, 23, 4)]
    public void Leaving_liquid_clamps_remaining_jump_to_one_fifth_of_active_height(
        int remaining,
        int jumpHeight,
        int expected)
    {
        var previous = new VanillaServerPlayerLiquidState(Wet: true, Lava: false, Honey: false, Shimmer: false);
        VanillaServerPlayerLiquidState current = VanillaServerPlayerLiquidState.Dry;

        int result = VanillaServerPlayerLiquidPhysics.ClampRemainingJumpOnLiquidExit(
            remaining,
            in previous,
            in current,
            jumpHeight);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Staying_wet_does_not_clamp_remaining_jump()
    {
        var previous = new VanillaServerPlayerLiquidState(Wet: true, Lava: false, Honey: false, Shimmer: false);
        var current = new VanillaServerPlayerLiquidState(Wet: true, Lava: false, Honey: false, Shimmer: false);

        int result = VanillaServerPlayerLiquidPhysics.ClampRemainingJumpOnLiquidExit(
            30,
            in previous,
            in current,
            30);

        Assert.Equal(30, result);
    }

    [Fact]
    public void Specialized_liquid_flag_without_wet_is_rejected()
    {
        var invalid = new VanillaServerPlayerLiquidState(Wet: false, Lava: false, Honey: true, Shimmer: false);

        Assert.Throws<ArgumentException>(() =>
            VanillaServerPlayerLiquidPhysics.ResolveMotionProfile(in invalid));
    }
}
