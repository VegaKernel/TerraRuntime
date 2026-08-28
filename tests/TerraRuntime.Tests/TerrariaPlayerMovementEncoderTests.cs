using System.Buffers;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlayerMovementEncoderTests
{
    [Fact]
    public void Encodes_authoritative_slot_and_optional_movement_fields_through_multiplicity()
    {
        var movement = new TerrariaPlayerMovementState(
            PlayerId: 7,
            ControlFlags: 0x53,
            MovementFlags: 0b1000_0100,
            MiscFlags1: 0b0100_0000,
            MiscFlags2: 0b0010_0000,
            SelectedItem: 12,
            PositionX: 10f,
            PositionY: 20f,
            HasVelocity: true,
            VelocityX: 1.5f,
            VelocityY: -2.5f,
            HasMount: true,
            MountType: 17,
            HasPotionOfReturnPositions: true,
            PotionOfReturnOriginalPositionX: 30f,
            PotionOfReturnOriginalPositionY: 40f,
            PotionOfReturnHomePositionX: 50f,
            PotionOfReturnHomePositionY: 60f,
            HasCameraTarget: true,
            CameraTargetX: 70f,
            CameraTargetY: 80f);

        byte[] encoded = TerrariaPlayerMovementEncoder.Encode(in movement);
        var input = new ReadOnlySequence<byte>(encoded);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.True(input.IsEmpty);
        Assert.Equal((byte)TerrariaMessageId.PlayerControls, frame.MessageId);
        Assert.True(frame.Payload.IsSingleSegment);

        PlayerUpdateView view = PlayerUpdateView.FromPayload(frame.Payload.FirstSpan);
        Assert.Equal((byte)7, view.PlayerId);
        Assert.Equal((byte)0x53, (byte)view.ControlFlags);
        Assert.Equal(12, view.SelectedItem);
        Assert.Equal(10f, view.PositionX);
        Assert.Equal(20f, view.PositionY);
        Assert.True(view.HasVelocity);
        Assert.Equal(1.5f, view.VelocityX);
        Assert.Equal(-2.5f, view.VelocityY);
        Assert.True(view.HasMount);
        Assert.Equal((ushort)17, view.MountType);
        Assert.True(view.HasPotionOfReturnPositions);
        Assert.Equal(30f, view.PotionOfReturnOriginalPositionX);
        Assert.Equal(60f, view.PotionOfReturnHomePositionY);
        Assert.True(view.HasCameraTarget);
        Assert.Equal(70f, view.CameraTargetX);
        Assert.Equal(80f, view.CameraTargetY);
    }
}
