using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaWorldItemOwnerDecoderTests
{
    [Fact]
    public void Item_owner_decodes_reservation_and_grab_delay_state()
    {
        byte[] payload = BuildPayload(
            itemIndex: 17,
            ownerPlayerId: 4,
            reservationTime: 12_345,
            grabDelayPlayer: 7,
            grabDelayTime: 67_890,
            positionX: 100.25f,
            positionY: -20.5f);
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.WorldItemOwner, new ReadOnlySequence<byte>(payload));

        TerrariaWorldItemOwnerDecodeResult result = TerrariaWorldItemOwnerDecoder.TryDecode(in frame, out var state);

        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.Decoded, result);
        Assert.Equal((short)17, state.ItemIndex);
        Assert.Equal((byte)4, state.OwnerPlayerId);
        Assert.Equal(12_345, state.TimeToKeepReservation);
        Assert.Equal((byte)7, state.GrabDelayPlayer);
        Assert.Equal(67_890, state.GrabDelayTime);
        Assert.Equal(100.25f, state.PositionX);
        Assert.Equal(-20.5f, state.PositionY);
    }

    [Fact]
    public void Segmented_minimal_payload_decodes()
    {
        byte[] payload = BuildPayload(
            itemIndex: 0,
            ownerPlayerId: byte.MaxValue,
            reservationTime: 0,
            grabDelayPlayer: byte.MaxValue,
            grabDelayTime: 0,
            positionX: 0f,
            positionY: 0f);
        ReadOnlySequence<byte> sequence = Segmented(payload, 3, 9);
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.WorldItemOwner, sequence);

        TerrariaWorldItemOwnerDecodeResult result = TerrariaWorldItemOwnerDecoder.TryDecode(in frame, out var state);

        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.Decoded, result);
        Assert.Equal(TerrariaWorldItemOwnerDecoder.MinimumPayloadLength, payload.Length);
        Assert.Equal(byte.MaxValue, state.OwnerPlayerId);
        Assert.Equal(byte.MaxValue, state.GrabDelayPlayer);
    }

    [Fact]
    public void Wrong_message_and_out_of_range_lengths_are_rejected_before_materialization()
    {
        TerrariaFrame wrong = Frame(
            (byte)TerrariaMessageId.WorldItemDrop,
            new ReadOnlySequence<byte>(new byte[TerrariaWorldItemOwnerDecoder.MinimumPayloadLength]));
        TerrariaFrame shortFrame = Frame(
            (byte)TerrariaMessageId.WorldItemOwner,
            new ReadOnlySequence<byte>(new byte[TerrariaWorldItemOwnerDecoder.MinimumPayloadLength - 1]));
        TerrariaFrame longFrame = Frame(
            (byte)TerrariaMessageId.WorldItemOwner,
            new ReadOnlySequence<byte>(new byte[TerrariaWorldItemOwnerDecoder.MaximumPayloadLength + 1]));

        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.WrongMessageId,
            TerrariaWorldItemOwnerDecoder.TryDecode(in wrong, out _));
        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.InvalidPayloadLength,
            TerrariaWorldItemOwnerDecoder.TryDecode(in shortFrame, out _));
        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.InvalidPayloadLength,
            TerrariaWorldItemOwnerDecoder.TryDecode(in longFrame, out _));
    }

    [Fact]
    public void Invalid_runtime_identity_and_negative_timer_are_invalid_state()
    {
        byte[] invalidIndex = BuildPayload(
            itemIndex: 400,
            ownerPlayerId: 1,
            reservationTime: 0,
            grabDelayPlayer: 1,
            grabDelayTime: 0,
            positionX: 0f,
            positionY: 0f);
        byte[] negativeTimer = BuildPayload(
            itemIndex: 1,
            ownerPlayerId: 1,
            reservationTime: -1,
            grabDelayPlayer: 1,
            grabDelayTime: 0,
            positionX: 0f,
            positionY: 0f);

        TerrariaFrame invalidIndexFrame = Frame(
            (byte)TerrariaMessageId.WorldItemOwner,
            new ReadOnlySequence<byte>(invalidIndex));
        TerrariaFrame negativeTimerFrame = Frame(
            (byte)TerrariaMessageId.WorldItemOwner,
            new ReadOnlySequence<byte>(negativeTimer));

        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.InvalidState,
            TerrariaWorldItemOwnerDecoder.TryDecode(in invalidIndexFrame, out _));
        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.InvalidState,
            TerrariaWorldItemOwnerDecoder.TryDecode(in negativeTimerFrame, out _));
    }

    [Fact]
    public void Invalid_seven_bit_integer_is_malformed()
    {
        byte[] payload = new byte[TerrariaWorldItemOwnerDecoder.MinimumPayloadLength];
        payload[0] = 1;
        payload[1] = 0;
        payload[2] = 1;
        for (int i = 3; i < 8; i++)
            payload[i] = 0xFF;
        payload[8] = 0xFF;
        TerrariaFrame frame = Frame(
            (byte)TerrariaMessageId.WorldItemOwner,
            new ReadOnlySequence<byte>(payload));

        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.Malformed,
            TerrariaWorldItemOwnerDecoder.TryDecode(in frame, out _));
    }

    private static byte[] BuildPayload(
        short itemIndex,
        byte ownerPlayerId,
        int reservationTime,
        byte grabDelayPlayer,
        int grabDelayTime,
        float positionX,
        float positionY)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(itemIndex);
        writer.Write(ownerPlayerId);
        Write7BitEncodedInt(writer, reservationTime);
        writer.Write(grabDelayPlayer);
        Write7BitEncodedInt(writer, grabDelayTime);
        writer.Write(positionX);
        writer.Write(positionY);
        return stream.ToArray();
    }

    private static void Write7BitEncodedInt(BinaryWriter writer, int value)
    {
        uint remaining = unchecked((uint)value);
        while (remaining >= 0x80)
        {
            writer.Write((byte)(remaining | 0x80));
            remaining >>= 7;
        }
        writer.Write((byte)remaining);
    }

    private static TerrariaFrame Frame(byte messageId, ReadOnlySequence<byte> payload) =>
        new(
            PacketLength: checked((ushort)(payload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength)),
            MessageId: messageId,
            Packet: payload,
            Payload: payload);

    private static ReadOnlySequence<byte> Segmented(byte[] payload, int firstLength, int secondLength)
    {
        var first = new Segment(payload.AsMemory(0, firstLength));
        var second = first.Append(payload.AsMemory(firstLength, secondLength - firstLength));
        Segment last = second.Append(payload.AsMemory(secondLength));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }
    }
}
