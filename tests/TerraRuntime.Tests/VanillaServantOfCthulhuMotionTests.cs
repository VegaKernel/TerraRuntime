using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaServantOfCthulhuMotionTests
{
    [Fact]
    public void Servant_accelerates_toward_quantized_target_at_verified_rate()
    {
        var input = new VanillaServantOfCthulhuMotionInput(
            NpcCenterX: 103f,
            NpcCenterY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            TargetCenterX: 203f,
            TargetCenterY: 100f);

        Assert.True(VanillaServantOfCthulhuMotion.TryStep(in input, out VanillaServantOfCthulhuMotionResult result));

        Assert.Equal(0.03f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
    }

    [Fact]
    public void Servant_uses_double_axis_acceleration_while_crossing_zero()
    {
        var input = new VanillaServantOfCthulhuMotionInput(
            NpcCenterX: 100f,
            NpcCenterY: 100f,
            VelocityX: -0.10f,
            VelocityY: 0f,
            TargetCenterX: 200f,
            TargetCenterY: 100f);

        Assert.True(VanillaServantOfCthulhuMotion.TryStep(in input, out VanillaServantOfCthulhuMotionResult result));

        Assert.Equal(-0.04f, result.VelocityX, 5);
    }

    [Fact]
    public void Same_quantized_cell_preserves_velocity()
    {
        var input = new VanillaServantOfCthulhuMotionInput(
            NpcCenterX: 101f,
            NpcCenterY: 101f,
            VelocityX: 1.25f,
            VelocityY: -0.75f,
            TargetCenterX: 103f,
            TargetCenterY: 102f);

        Assert.True(VanillaServantOfCthulhuMotion.TryStep(in input, out VanillaServantOfCthulhuMotionResult result));

        Assert.Equal(input.VelocityX, result.VelocityX);
        Assert.Equal(input.VelocityY, result.VelocityY);
    }

    [Fact]
    public void Non_finite_input_is_rejected()
    {
        var input = new VanillaServantOfCthulhuMotionInput(
            NpcCenterX: float.NaN,
            NpcCenterY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            TargetCenterX: 0f,
            TargetCenterY: 0f);

        Assert.False(VanillaServantOfCthulhuMotion.TryStep(in input, out _));
    }
}
