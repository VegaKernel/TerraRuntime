using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaDoorToggleCodecTests
{
    [Fact]
    public void Open_door_payload_matches_packet19_layout_and_round_trips()
    {
        var expected = new TerrariaDoorToggleState(
            (byte)TerrariaDoorToggleAction.OpenDoor,
            TileX: 123,
            TileY: 456,
            DirectionX: 1);

        Assert.Equal(
            TerrariaDoorToggleEncodeResult.Encoded,
            TerrariaDoorToggleCodec.TryEncode(in expected, out byte[] packet));
        Assert.Equal(9, packet.Length);
        Assert.Equal((ushort)9, BinaryPrimitives.ReadUInt16LittleEndian(packet));
        Assert.Equal((byte)TerrariaMessageId.DoorToggle, packet[2]);
        Assert.Equal((byte)TerrariaDoorToggleAction.OpenDoor, packet[3]);
        Assert.Equal((short)123, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4, 2)));
        Assert.Equal((short)456, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(6, 2)));
        Assert.Equal((byte)1, packet[8]);

        TerrariaFrame frame = Frame(packet);
        Assert.Equal(
            TerrariaDoorToggleDecodeResult.Decoded,
            TerrariaDoorToggleCodec.TryDecode(in frame, out TerrariaDoorToggleState actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Tall_gate_open_encodes_vanilla_default_direction_as_zero()
    {
        var expected = new TerrariaDoorToggleState(
            (byte)TerrariaDoorToggleAction.OpenTallGate,
            TileX: 77,
            TileY: 88,
            DirectionX: -1);

        Assert.Equal(
            TerrariaDoorToggleEncodeResult.Encoded,
            TerrariaDoorToggleCodec.TryEncode(in expected, out byte[] packet));
        Assert.Equal((byte)0, packet[8]);

        TerrariaFrame frame = Frame(packet);
        Assert.Equal(
            TerrariaDoorToggleDecodeResult.Decoded,
            TerrariaDoorToggleCodec.TryDecode(in frame, out TerrariaDoorToggleState actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Decode_treats_any_nonzero_direction_byte_as_positive_like_MessageBuffer()
    {
        byte[] payload = [0, 1, 0, 2, 0, 17];
        TerrariaFrame frame = new(
            checked((ushort)(payload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength)),
            (byte)TerrariaMessageId.DoorToggle,
            new ReadOnlySequence<byte>(payload),
            new ReadOnlySequence<byte>(payload));

        Assert.Equal(
            TerrariaDoorToggleDecodeResult.Decoded,
            TerrariaDoorToggleCodec.TryDecode(in frame, out TerrariaDoorToggleState actual));
        Assert.Equal(1, actual.DirectionX);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(7)]
    public void Only_six_byte_payload_is_accepted(int length)
    {
        byte[] payload = new byte[length];
        TerrariaFrame frame = new(
            checked((ushort)(length + TerrariaFrameDecoderOptions.MinimumFrameLength)),
            (byte)TerrariaMessageId.DoorToggle,
            new ReadOnlySequence<byte>(payload),
            new ReadOnlySequence<byte>(payload));

        Assert.Equal(
            TerrariaDoorToggleDecodeResult.InvalidPayloadLength,
            TerrariaDoorToggleCodec.TryDecode(in frame, out _));
    }

    [Fact]
    public void Encode_rejects_non_vanilla_direction()
    {
        var invalid = new TerrariaDoorToggleState(0, 1, 2, DirectionX: 0);

        Assert.Equal(
            TerrariaDoorToggleEncodeResult.InvalidState,
            TerrariaDoorToggleCodec.TryEncode(in invalid, out byte[] packet));
        Assert.Empty(packet);
    }

    private static TerrariaFrame Frame(byte[] packet)
    {
        var sequence = new ReadOnlySequence<byte>(packet);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(0, sequence.Length);
        return frame;
    }
}
