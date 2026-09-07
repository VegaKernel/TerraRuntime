using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class VanillaServerPlayerHorizontalControlTests
{
    [Theory]
    [InlineData(-1, -0.08f)]
    [InlineData(1, 0.08f)]
    public void Resting_player_accelerates_by_vanilla_amount(int rawIntent, float expected)
    {
        var intent = (ServerPlayerHorizontalIntent)rawIntent;
        Assert.Equal(expected, VanillaServerPlayerHorizontalControl.Apply(0f, 0f, intent), 5);
    }

    [Fact]
    public void Reversing_rightward_motion_left_applies_slowdown_then_acceleration()
    {
        float next = VanillaServerPlayerHorizontalControl.Apply(0.5f, 0f, ServerPlayerHorizontalIntent.Left);
        Assert.Equal(0.22f, next, 5);
    }

    [Fact]
    public void Reversing_leftward_motion_right_is_symmetric()
    {
        float next = VanillaServerPlayerHorizontalControl.Apply(-0.5f, 0f, ServerPlayerHorizontalIntent.Right);
        Assert.Equal(-0.22f, next, 5);
    }

    [Fact]
    public void Same_direction_input_at_or_above_max_speed_falls_through_to_ground_slowdown()
    {
        Assert.Equal(2.8f, VanillaServerPlayerHorizontalControl.Apply(3f, 0f, ServerPlayerHorizontalIntent.Right), 5);
        Assert.Equal(2.87f, VanillaServerPlayerHorizontalControl.Apply(3.07f, 0f, ServerPlayerHorizontalIntent.Right), 5);
        Assert.Equal(-2.8f, VanillaServerPlayerHorizontalControl.Apply(-3f, 0f, ServerPlayerHorizontalIntent.Left), 5);
    }

    [Fact]
    public void Acceleration_can_cross_max_speed_because_ordinary_path_has_no_general_clamp()
    {
        Assert.Equal(3.07f, VanillaServerPlayerHorizontalControl.Apply(2.99f, 0f, ServerPlayerHorizontalIntent.Right), 5);
        Assert.Equal(-3.07f, VanillaServerPlayerHorizontalControl.Apply(-2.99f, 0f, ServerPlayerHorizontalIntent.Left), 5);
    }

    [Fact]
    public void Grounded_stop_uses_full_vanilla_run_slowdown()
    {
        Assert.Equal(0.8f, VanillaServerPlayerHorizontalControl.Apply(1f, 0f, ServerPlayerHorizontalIntent.Stop), 5);
        Assert.Equal(-0.8f, VanillaServerPlayerHorizontalControl.Apply(-1f, 0f, ServerPlayerHorizontalIntent.Stop), 5);
        Assert.Equal(0f, VanillaServerPlayerHorizontalControl.Apply(0.15f, 0f, ServerPlayerHorizontalIntent.Stop), 5);
    }

    [Fact]
    public void Airborne_stop_uses_half_vanilla_run_slowdown()
    {
        Assert.Equal(0.9f, VanillaServerPlayerHorizontalControl.Apply(1f, 1f, ServerPlayerHorizontalIntent.Stop), 5);
        Assert.Equal(-0.9f, VanillaServerPlayerHorizontalControl.Apply(-1f, -1f, ServerPlayerHorizontalIntent.Stop), 5);
        Assert.Equal(0f, VanillaServerPlayerHorizontalControl.Apply(0.05f, 1f, ServerPlayerHorizontalIntent.Stop), 5);
    }

    [Fact]
    public void Constants_pin_official_terraria_server_1458_horizontal_baseline()
    {
        Assert.Equal(3f, VanillaServerPlayerHorizontalControl.MaximumRunSpeed);
        Assert.Equal(0.08f, VanillaServerPlayerHorizontalControl.RunAcceleration);
        Assert.Equal(0.2f, VanillaServerPlayerHorizontalControl.RunSlowdown);
        Assert.Equal(0.1f, VanillaServerPlayerHorizontalControl.AirborneRunSlowdown);
    }

    [Fact]
    public void Terraspark_and_grounded_magiluminescence_profile_pins_verified_accessory_math()
    {
        VanillaServerPlayerHorizontalProfile1458 profile =
            VanillaServerPlayerHorizontalProfile1458.ResolveBotMobility(
                terrasparkBoots: true,
                magiluminescence: true,
                fishronWings: true,
                grounded: true);

        Assert.Equal(3.726f, profile.MaximumRunSpeed, 5);
        Assert.Equal(7.7625f, profile.AcceleratedRunSpeed, 5);
        Assert.Equal(0.1512f, profile.RunAcceleration, 5);
        Assert.Equal(0.35f, profile.RunSlowdown, 5);
        Assert.True(profile.WingHorizontalAcceleration);

        float velocity = 0f;
        for (int tick = 0; tick < 100; tick++)
        {
            velocity = VanillaServerPlayerHorizontalControl.Apply(
                velocity,
                velocityY: 0f,
                ServerPlayerHorizontalIntent.Right,
                in profile);
        }
        Assert.True(velocity > 6.5f, $"Accessory-equipped player only reached {velocity} px/tick.");
    }

    [Fact]
    public void Magiluminescence_ground_effect_is_independent_and_air_effect_stays_absent()
    {
        VanillaServerPlayerHorizontalProfile1458 grounded =
            VanillaServerPlayerHorizontalProfile1458.ResolveBotMobility(
                terrasparkBoots: false,
                magiluminescence: true,
                fishronWings: false,
                grounded: true);
        Assert.Equal(3.45f, grounded.MaximumRunSpeed, 5);
        Assert.Equal(3.45f, grounded.AcceleratedRunSpeed, 5);
        Assert.Equal(0.14f, grounded.RunAcceleration, 5);
        Assert.Equal(0.35f, grounded.RunSlowdown, 5);

        VanillaServerPlayerHorizontalProfile1458 airborne =
            VanillaServerPlayerHorizontalProfile1458.ResolveBotMobility(
                terrasparkBoots: false,
                magiluminescence: true,
                fishronWings: false,
                grounded: false);
        Assert.Equal(VanillaServerPlayerHorizontalProfile1458.Baseline, airborne);
    }
}
