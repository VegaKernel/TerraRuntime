using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlayerEquipmentCodecTests
{
    [Fact]
    public void Round_trips_packet5_through_multiplicity()
    {
        var equipment = new TerrariaPlayerEquipmentState(
            PlayerId: 9,
            SlotId: 123,
            Stack: 42,
            Prefix: 7,
            ItemNetId: 314,
            ItemFlags: 3);

        byte[] encoded = TerrariaPlayerEquipmentCodec.Encode(in equipment);
        var input = new ReadOnlySequence<byte>(encoded);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.True(input.IsEmpty);
        Assert.Equal((byte)TerrariaMessageId.SyncEquipment, frame.MessageId);
        Assert.Equal(
            TerrariaPlayerEquipmentDecodeResult.Decoded,
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out TerrariaPlayerEquipmentState decoded));
        Assert.Equal(equipment, decoded);
    }

    [Fact]
    public void Rejects_wrong_packet5_payload_length()
    {
        byte[] encoded = new byte[TerrariaPlayerEquipmentCodec.PayloadLength + 2];
        int frameLength = encoded.Length;
        encoded[0] = (byte)frameLength;
        encoded[1] = (byte)(frameLength >> 8);
        encoded[2] = (byte)TerrariaMessageId.SyncEquipment;
        var input = new ReadOnlySequence<byte>(encoded);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.Equal(
            TerrariaPlayerEquipmentDecodeResult.InvalidPayloadLength,
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out _));
    }
}
