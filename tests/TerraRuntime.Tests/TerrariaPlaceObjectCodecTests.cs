using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlaceObjectCodecTests
{
    [Fact]
    public void Exact_payload_decodes()
    {
        byte[] payload =
        [
            0x34, 0x12,
            0x78, 0x56,
            0x15, 0x00,
            0x02, 0x00,
            3,
            0xFF,
            1
        ];
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.PlaceObject, new ReadOnlySequence<byte>(payload));

        TerrariaPlaceObjectDecodeResult result = TerrariaPlaceObjectCodec.TryDecode(
            in frame,
            out TerrariaPlaceObjectState state);

        Assert.Equal(TerrariaPlaceObjectDecodeResult.Decoded, result);
        Assert.Equal((short)0x1234, state.TileX);
        Assert.Equal((short)0x5678, state.TileY);
        Assert.Equal((short)21, state.TileType);
        Assert.Equal((short)2, state.Style);
        Assert.Equal((byte)3, state.Alternate);
        Assert.Equal((sbyte)-1, state.Random);
        Assert.True(state.Direction);
    }

    [Fact]
    public void Segmented_payload_decodes()
    {
        var expected = new TerrariaPlaceObjectState(-100, 42, 21, 0, 0, -1, false);
        Assert.Equal(
            TerrariaPlaceObjectEncodeResult.Encoded,
            TerrariaPlaceObjectCodec.TryEncode(in expected, out byte[] encoded));
        byte[] payload = encoded[TerrariaFrameDecoderOptions.MinimumFrameLength..];
        TerrariaFrame frame = Frame(
            (byte)TerrariaMessageId.PlaceObject,
            Segmented(payload, 3, 9));

        Assert.Equal(
            TerrariaPlaceObjectDecodeResult.Decoded,
            TerrariaPlaceObjectCodec.TryDecode(in frame, out TerrariaPlaceObjectState actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(12)]
    public void Only_eleven_byte_payload_is_accepted(int length)
    {
        TerrariaFrame frame = Frame(
            (byte)TerrariaMessageId.PlaceObject,
            new ReadOnlySequence<byte>(new byte[length]));

        Assert.Equal(
            TerrariaPlaceObjectDecodeResult.InvalidPayloadLength,
            TerrariaPlaceObjectCodec.TryDecode(in frame, out _));
    }

    [Fact]
    public void Non_boolean_direction_is_rejected()
    {
        byte[] payload = new byte[TerrariaPlaceObjectCodec.PayloadLength];
        payload[10] = 2;
        TerrariaFrame frame = Frame(
            (byte)TerrariaMessageId.PlaceObject,
            new ReadOnlySequence<byte>(payload));

        Assert.Equal(
            TerrariaPlaceObjectDecodeResult.InvalidDirectionValue,
            TerrariaPlaceObjectCodec.TryDecode(in frame, out _));
    }

    [Fact]
    public void Wrong_message_is_rejected()
    {
        TerrariaFrame frame = Frame(
            (byte)TerrariaMessageId.TileManipulation,
            new ReadOnlySequence<byte>(new byte[TerrariaPlaceObjectCodec.PayloadLength]));

        Assert.Equal(
            TerrariaPlaceObjectDecodeResult.WrongMessageId,
            TerrariaPlaceObjectCodec.TryDecode(in frame, out _));
    }

    [Fact]
    public void Encode_matches_verified_layout_and_round_trips()
    {
        var expected = new TerrariaPlaceObjectState(-123, 456, 21, 0, 0, -1, true);

        TerrariaPlaceObjectEncodeResult result = TerrariaPlaceObjectCodec.TryEncode(
            in expected,
            out byte[] packet);

        Assert.Equal(TerrariaPlaceObjectEncodeResult.Encoded, result);
        Assert.Equal(14, packet.Length);
        Assert.Equal((ushort)14, BinaryPrimitives.ReadUInt16LittleEndian(packet));
        Assert.Equal((byte)TerrariaMessageId.PlaceObject, packet[2]);
        Assert.Equal(expected.TileX, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(3, 2)));
        Assert.Equal(expected.TileY, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(5, 2)));
        Assert.Equal(expected.TileType, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(7, 2)));
        Assert.Equal(expected.Style, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(9, 2)));
        Assert.Equal(expected.Alternate, packet[11]);
        Assert.Equal(unchecked((byte)expected.Random), packet[12]);
        Assert.Equal(1, packet[13]);

        TerrariaFrame frame = new(
            checked((ushort)packet.Length),
            packet[2],
            new ReadOnlySequence<byte>(packet),
            new ReadOnlySequence<byte>(packet.AsMemory(3)));
        Assert.Equal(
            TerrariaPlaceObjectDecodeResult.Decoded,
            TerrariaPlaceObjectCodec.TryDecode(in frame, out TerrariaPlaceObjectState actual));
        Assert.Equal(expected, actual);
    }

    private static TerrariaFrame Frame(byte messageId, ReadOnlySequence<byte> payload) =>
        new(
            checked((ushort)(payload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength)),
            messageId,
            payload,
            payload);

    private static ReadOnlySequence<byte> Segmented(byte[] payload, int firstLength, int secondLength)
    {
        var first = new Segment(payload.AsMemory(0, firstLength));
        var second = first.Append(payload.AsMemory(firstLength, secondLength - firstLength));
        Segment last = second.Append(payload.AsMemory(secondLength));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
