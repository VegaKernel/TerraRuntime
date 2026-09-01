using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaNpcDamageCodecTests
{
    [Fact]
    public void Packet_28_round_trips_source_wire_shape()
    {
        var state = new TerrariaNpcDamageState(
            NpcSlot: 17,
            Generation: 9,
            Damage: 123,
            KnockBack: 4.5f,
            HitDirectionWire: 0,
            CriticalRaw: 1);

        Assert.Equal(TerrariaNpcDamageEncodeResult.Encoded, TerrariaNpcDamageCodec.TryEncode(in state, out byte[] encoded));
        var sequence = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(TerrariaNpcDamageDecodeResult.Decoded, TerrariaNpcDamageCodec.TryDecode(in frame, out TerrariaNpcDamageState decoded));
        Assert.Equal(state, decoded);
        Assert.Equal(-1, decoded.HitDirection);
        Assert.True(decoded.Critical);
    }

    [Fact]
    public void Ack_is_empty_packet_162_frame()
    {
        Assert.Equal(TerrariaNpcDamageEncodeResult.Encoded, TerrariaNpcDamageCodec.TryEncodeAck(out byte[] encoded));
        var sequence = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal((byte)TerrariaMessageId.NpcDamageAck, frame.MessageId);
        Assert.Equal(0, frame.Payload.Length);
    }
}
