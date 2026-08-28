using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlayerMovementDecoderTests
{
    [Fact]
    public void Decodes_minimum_player_update_payload()
    {
        byte[] payload = new byte[TerrariaPlayerMovementDecoder.MinimumPayloadLength];
        payload[0] = 7;
        payload[1] = 0b0101_0011;
        payload[2] = 0;
        payload[3] = 0;
        payload[4] = 0;
        payload[5] = 4;
        WriteSingle(payload.AsSpan(6), 123.5f);
        WriteSingle(payload.AsSpan(10), 456.25f);

        TerrariaPlayerMovementDecodeResult result = TerrariaPlayerMovementDecoder.TryDecode(
            Frame(payload),
            out TerrariaPlayerMovementRequest request);

        Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded, result);
        Assert.Equal((byte)7, request.ClaimedPlayerId);
        Assert.Equal((byte)0b0101_0011, request.ControlFlags);
        Assert.Equal((byte)4, request.SelectedItem);
        Assert.Equal(123.5f, request.PositionX);
        Assert.Equal(456.25f, request.PositionY);
        Assert.False(request.HasVelocity);
        Assert.False(request.HasMount);
        Assert.False(request.HasPotionOfReturnPositions);
        Assert.False(request.HasCameraTarget);
    }

    [Fact]
    public void Decodes_all_optional_player_update_fields()
    {
        byte[] payload = new byte[TerrariaPlayerMovementDecoder.MaximumPayloadLength];
        payload[0] = 9;
        payload[2] = 0b1000_0100; // velocity + mount
        payload[3] = 0b0100_0000; // Potion of Return positions
        payload[4] = 0b0010_0000; // camera target
        payload[5] = 12;

        int offset = 6;
        WriteSingle(payload.AsSpan(offset), 10f); offset += 4;
        WriteSingle(payload.AsSpan(offset), 20f); offset += 4;
        WriteSingle(payload.AsSpan(offset), 1.5f); offset += 4;
        WriteSingle(payload.AsSpan(offset), -2.5f); offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset), 17); offset += 2;
        WriteSingle(payload.AsSpan(offset), 30f); offset += 4;
        WriteSingle(payload.AsSpan(offset), 40f); offset += 4;
        WriteSingle(payload.AsSpan(offset), 50f); offset += 4;
        WriteSingle(payload.AsSpan(offset), 60f); offset += 4;
        WriteSingle(payload.AsSpan(offset), 70f); offset += 4;
        WriteSingle(payload.AsSpan(offset), 80f); offset += 4;
        Assert.Equal(payload.Length, offset);

        TerrariaPlayerMovementDecodeResult result = TerrariaPlayerMovementDecoder.TryDecode(
            Frame(payload),
            out TerrariaPlayerMovementRequest request);

        Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded, result);
        Assert.True(request.HasVelocity);
        Assert.Equal(1.5f, request.VelocityX);
        Assert.Equal(-2.5f, request.VelocityY);
        Assert.True(request.HasMount);
        Assert.Equal((ushort)17, request.MountType);
        Assert.True(request.HasPotionOfReturnPositions);
        Assert.Equal(30f, request.PotionOfReturnOriginalPositionX);
        Assert.Equal(60f, request.PotionOfReturnHomePositionY);
        Assert.True(request.HasCameraTarget);
        Assert.Equal(70f, request.CameraTargetX);
        Assert.Equal(80f, request.CameraTargetY);
    }

    [Fact]
    public void Rejects_non_finite_coordinates()
    {
        byte[] payload = new byte[TerrariaPlayerMovementDecoder.MinimumPayloadLength];
        WriteSingle(payload.AsSpan(6), float.NaN);
        WriteSingle(payload.AsSpan(10), 10f);

        TerrariaPlayerMovementDecodeResult result = TerrariaPlayerMovementDecoder.TryDecode(
            Frame(payload),
            out _);

        Assert.Equal(TerrariaPlayerMovementDecodeResult.NonFiniteValue, result);
    }

    [Fact]
    public void Rejects_trailing_bytes_not_selected_by_flags()
    {
        byte[] payload = new byte[TerrariaPlayerMovementDecoder.MinimumPayloadLength + 1];
        WriteSingle(payload.AsSpan(6), 1f);
        WriteSingle(payload.AsSpan(10), 2f);

        TerrariaPlayerMovementDecodeResult result = TerrariaPlayerMovementDecoder.TryDecode(
            Frame(payload),
            out _);

        Assert.Equal(TerrariaPlayerMovementDecodeResult.Malformed, result);
    }

    private static TerrariaFrame Frame(byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)TerrariaMessageId.PlayerControls,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private static void WriteSingle(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));
}
