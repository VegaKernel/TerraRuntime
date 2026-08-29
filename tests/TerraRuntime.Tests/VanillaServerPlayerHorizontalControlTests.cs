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
        float next = VanillaServerPlayerHorizontalControl.Apply(
            0.5f,
            0f,
            ServerPlayerHorizontalIntent.Left);

        Assert.Equal(0.22f, next, 5);
    }

    [Fact]
    public void Reversing_leftward_motion_right_is_symmetric()
    {
        float next = VanillaServerPlayerHorizontalControl.Apply(
            -0.5f,
            0f,
            ServerPlayerHorizontalIntent.Right);

        Assert.Equal(-0.22f, next, 5);
    }

    [Fact]
    public void Grounded_stop_uses_full_vanilla_run_slowdown()
    {
        Assert.Equal(
            0.8f,
            VanillaServerPlayerHorizontalControl.Apply(1f, 0f, ServerPlayerHorizontalIntent.Stop),
            5);
        Assert.Equal(
            -0.8f,
            VanillaServerPlayerHorizontalControl.Apply(-1f, 0f, ServerPlayerHorizontalIntent.Stop),
            5);
        Assert.Equal(
            0f,
            VanillaServerPlayerHorizontalControl.Apply(0.15f, 0f, ServerPlayerHorizontalIntent.Stop),
            5);
    }

    [Fact]
    public void Airborne_stop_uses_half_vanilla_run_slowdown()
    {
        Assert.Equal(
            0.9f,
            VanillaServerPlayerHorizontalControl.Apply(1f, 1f, ServerPlayerHorizontalIntent.Stop),
            5);
        Assert.Equal(
            -0.9f,
            VanillaServerPlayerHorizontalControl.Apply(-1f, -1f, ServerPlayerHorizontalIntent.Stop),
            5);
        Assert.Equal(
            0f,
            VanillaServerPlayerHorizontalControl.Apply(0.05f, 1f, ServerPlayerHorizontalIntent.Stop),
            5);
    }

    [Fact]
    public void Constants_pin_official_terraria_server_1458_horizontal_baseline()
    {
        Assert.Equal(3f, VanillaServerPlayerHorizontalControl.MaximumRunSpeed);
        Assert.Equal(0.08f, VanillaServerPlayerHorizontalControl.RunAcceleration);
        Assert.Equal(0.2f, VanillaServerPlayerHorizontalControl.RunSlowdown);
        Assert.Equal(0.1f, VanillaServerPlayerHorizontalControl.AirborneRunSlowdown);
    }
}
