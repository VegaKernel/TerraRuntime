using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaTileManipulationCodecTests
{
    [Fact]
    public void Exact_payload_decodes()
    {
        byte[] payload = [7, 0x34, 0x12, 0x78, 0x56, 0xFE, 0xFF, 9];
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.TileManipulation, new ReadOnlySequence<byte>(payload));

        TerrariaTileManipulationDecodeResult result = TerrariaTileManipulationCodec.TryDecode(
            in frame,
            out TerrariaTileManipulationState state);

        Assert.Equal(TerrariaTileManipulationDecodeResult.Decoded, result);
        Assert.Equal((byte)7, state.Action);
        Assert.Equal((short)0x1234, state.TileX);
        Assert.Equal((short)0x5678, state.TileY);
        Assert.Equal((short)-2, state.Data);
        Assert.Equal((byte)9, state.Style);
    }

    [Fact]
    public void Segmented_payload_decodes()
    {
        byte[] payload = [3, 0x9C, 0xFF, 0x2A, 0, 0x10, 0x27, 4];
        TerrariaFrame frame = Frame(
            (byte)TerrariaMessageId.TileManipulation,
            Segmented(payload, 1, 5));

        Assert.Equal(
            TerrariaTileManipulationDecodeResult.Decoded,
            TerrariaTileManipulationCodec.TryDecode(in frame, out TerrariaTileManipulationState state));
        Assert.Equal((byte)3, state.Action);
        Assert.Equal((short)-100, state.TileX);
        Assert.Equal((short)42, state.TileY);
        Assert.Equal((short)10_000, state.Data);
        Assert.Equal((byte)4, state.Style);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void Only_eight_byte_payload_is_accepted(int length)
    {
        TerrariaFrame frame = Frame(
            (byte)TerrariaMessageId.TileManipulation,
            new ReadOnlySequence<byte>(new byte[length]));

        Assert.Equal(
            TerrariaTileManipulationDecodeResult.InvalidPayloadLength,
            TerrariaTileManipulationCodec.TryDecode(in frame, out _));
    }

    [Fact]
    public void Wrong_message_is_rejected()
    {
        TerrariaFrame frame = Frame(
            (byte)TerrariaMessageId.TileSquare,
            new ReadOnlySequence<byte>(new byte[TerrariaTileManipulationCodec.PayloadLength]));

        Assert.Equal(
            TerrariaTileManipulationDecodeResult.WrongMessageId,
            TerrariaTileManipulationCodec.TryDecode(in frame, out _));
    }

    [Theory]
    [InlineData((byte)TerrariaTileManipulationAction.KillTile, true)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceTile, true)]
    [InlineData((byte)TerrariaTileManipulationAction.KillWall, false)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceWall, false)]
    [InlineData((byte)TerrariaTileManipulationAction.KillTileNoItem, false)]
    [InlineData(255, false)]
    public void Runtime_action_admission_is_explicit_and_fail_closed(byte rawAction, bool admitted)
    {
        var state = new TerrariaTileManipulationState(rawAction, 10, 10, 0, 0);

        bool resolved = state.TryGetKnownAction(out TerrariaTileManipulationAction action);

        Assert.Equal(admitted, resolved);
        if (admitted)
            Assert.Equal(rawAction, (byte)action);
    }

    [Theory]
    [InlineData((byte)TerrariaTileManipulationAction.KillWall)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceWall)]
    [InlineData((byte)TerrariaTileManipulationAction.KillTileNoItem)]
    public void Wire_known_but_unproven_actions_remain_decodable_while_runtime_authority_is_disabled(byte rawAction)
    {
        byte[] payload = [rawAction, 10, 0, 20, 0, 1, 0, 0];
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.TileManipulation, new ReadOnlySequence<byte>(payload));

        Assert.Equal(
            TerrariaTileManipulationDecodeResult.Decoded,
            TerrariaTileManipulationCodec.TryDecode(in frame, out TerrariaTileManipulationState state));
        Assert.Equal(rawAction, state.Action);
        Assert.False(state.TryGetKnownAction(out _));
    }

    [Fact]
    public void Encode_matches_verified_layout_and_round_trips()
    {
        var expected = new TerrariaTileManipulationState(5, -123, 456, 789, 11);

        TerrariaTileManipulationEncodeResult result = TerrariaTileManipulationCodec.TryEncode(
            in expected,
            out byte[] packet);

        Assert.Equal(TerrariaTileManipulationEncodeResult.Encoded, result);
        Assert.Equal(11, packet.Length);
        Assert.Equal((ushort)11, BinaryPrimitives.ReadUInt16LittleEndian(packet));
        Assert.Equal((byte)TerrariaMessageId.TileManipulation, packet[2]);
        Assert.Equal(expected.Action, packet[3]);
        Assert.Equal(expected.TileX, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4, 2)));
        Assert.Equal(expected.TileY, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(6, 2)));
        Assert.Equal(expected.Data, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(8, 2)));
        Assert.Equal(expected.Style, packet[10]);

        TerrariaFrame frame = new(
            checked((ushort)packet.Length),
            packet[2],
            new ReadOnlySequence<byte>(packet),
            new ReadOnlySequence<byte>(packet.AsMemory(3)));
        Assert.Equal(
            TerrariaTileManipulationDecodeResult.Decoded,
            TerrariaTileManipulationCodec.TryDecode(in frame, out TerrariaTileManipulationState actual));
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
