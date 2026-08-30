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

    [Fact]
    public void Named_presence_flags_enable_their_exact_optional_payloads()
    {
        PlayerMovementCommitRequest request = Request() with
        {
            MovementFlags = (byte)(VanillaPlayerMovementNormalizer.MovementVelocityPresentFlag |
                                   VanillaPlayerMovementNormalizer.MovementMountPresentFlag),
            MiscFlags1 = VanillaPlayerMovementNormalizer.Misc1PotionOfReturnPositionsPresentFlag,
            MiscFlags2 = VanillaPlayerMovementNormalizer.Misc2CameraTargetPresentFlag,
            VelocityX = 1.5f,
            VelocityY = -2.5f,
            MountType = 1,
            PotionOfReturnOriginalPositionX = 10f,
            PotionOfReturnOriginalPositionY = 20f,
            PotionOfReturnHomePositionX = 30f,
            PotionOfReturnHomePositionY = 40f,
            CameraTargetX = 50f,
            CameraTargetY = 60f
        };

        Assert.True(VanillaPlayerMovementNormalizer.TryNormalize(in request, out PlayerMovementCommitRequest actual));
        Assert.True(actual.HasVelocity);
        Assert.Equal(1.5f, actual.VelocityX);
        Assert.Equal(-2.5f, actual.VelocityY);
        Assert.True(actual.HasMount);
        Assert.Equal((ushort)1, actual.MountType);
        Assert.True(actual.HasPotionOfReturnPositions);
        Assert.Equal(10f, actual.PotionOfReturnOriginalPositionX);
        Assert.True(actual.HasCameraTarget);
        Assert.Equal(50f, actual.CameraTargetX);
    }

    [Theory]
    [InlineData(59, 0, 0, 0)]
    [InlineData(0, VanillaPlayerMovementNormalizer.MovementVelocityPresentFlag, 0, 0)]
    [InlineData(0, VanillaPlayerMovementNormalizer.MovementMountPresentFlag, 0, 0)]
    [InlineData(0, 0, VanillaPlayerMovementNormalizer.Misc1PotionOfReturnPositionsPresentFlag, 0)]
    [InlineData(0, 0, 0, VanillaPlayerMovementNormalizer.Misc2CameraTargetPresentFlag)]
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
