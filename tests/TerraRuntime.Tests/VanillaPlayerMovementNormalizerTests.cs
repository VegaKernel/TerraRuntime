using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaPlayerMovementNormalizerTests
{
    [Fact]
    public void Optional_field_presence_is_derived_from_packet_flags()
    {
        PlayerMovementCommitRequest request = Request() with
        {
            HasVelocity = true,
            VelocityX = float.NaN,
            HasMount = true,
            MountType = ushort.MaxValue,
            HasPotionOfReturnPositions = true,
            PotionOfReturnOriginalPositionX = float.NaN,
            HasCameraTarget = true,
            CameraTargetX = float.NaN
        };

        Assert.True(VanillaPlayerMovementNormalizer.TryNormalize(in request, out PlayerMovementCommitRequest actual));
        Assert.False(actual.HasVelocity);
        Assert.Equal(0f, actual.VelocityX);
        Assert.False(actual.HasMount);
        Assert.Equal((ushort)0, actual.MountType);
        Assert.False(actual.HasPotionOfReturnPositions);
        Assert.False(actual.HasCameraTarget);
    }

    [Theory]
    [InlineData(59, 0, 0, 0)]
    [InlineData(0, 4, 0, 0)]
    [InlineData(0, 128, 0, 0)]
    [InlineData(0, 0, 64, 0)]
    [InlineData(0, 0, 0, 32)]
    public void Rejects_invalid_selected_item_or_present_optional_state(
        byte selectedItem,
        byte movementFlags,
        byte miscFlags1,
        byte miscFlags2)
    {
        PlayerMovementCommitRequest request = Request() with
        {
            SelectedItem = selectedItem,
            MovementFlags = movementFlags,
            MiscFlags1 = miscFlags1,
            MiscFlags2 = miscFlags2,
            VelocityX = float.NaN,
            MountType = VanillaPlayerMovementNormalizer.MountTypeCount,
            PotionOfReturnOriginalPositionX = float.NaN,
            CameraTargetX = float.NaN
        };

        Assert.False(VanillaPlayerMovementNormalizer.TryNormalize(in request, out _));
    }

    internal static PlayerMovementCommitRequest Request() =>
        new(
            new(3),
            ControlFlags: 0,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: 100f,
            PositionY: 200f,
            HasVelocity: false,
            VelocityX: 0f,
            VelocityY: 0f,
            HasMount: false,
            MountType: 0,
            HasPotionOfReturnPositions: false,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            HasCameraTarget: false,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
}
