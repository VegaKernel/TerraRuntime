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
    public void Packet_28_matches_official_1458_golden_bytes()
    {
        // TerrariaServer 1.4.5.8 NetMessage packet 28 writes:
        // npc byte, generation byte, Int16 damage, Single knockBack, byte(direction + 1), crit byte.
        var state = new TerrariaNpcDamageState(
            NpcSlot: 17,
            Generation: 9,
            Damage: 123,
            KnockBack: 4.5f,
            HitDirectionWire: 0,
            CriticalRaw: 1);
        byte[] expected =
        [
            0x0D, 0x00, 0x1C,
            0x11, 0x09, 0x7B, 0x00,
            0x00, 0x00, 0x90, 0x40,
            0x00, 0x01
        ];

        Assert.Equal(TerrariaNpcDamageEncodeResult.Encoded, TerrariaNpcDamageCodec.TryEncode(in state, out byte[] encoded));
        Assert.Equal(expected, encoded);

        var sequence = new ReadOnlySequence<byte>(expected);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(TerrariaNpcDamageDecodeResult.Decoded, TerrariaNpcDamageCodec.TryDecode(in frame, out TerrariaNpcDamageState decoded));
        Assert.Equal(state, decoded);
    }

    [Fact]
    public void Ack_is_empty_packet_162_frame()
    {
        Assert.Equal(TerrariaNpcDamageEncodeResult.Encoded, TerrariaNpcDamageCodec.TryEncodeAck(out byte[] encoded));
        Assert.Equal(new byte[] { 0x03, 0x00, 0xA2 }, encoded);

        var sequence = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal((byte)TerrariaMessageId.NpcDamageAck, frame.MessageId);
        Assert.Equal(0, frame.Payload.Length);
    }
}
