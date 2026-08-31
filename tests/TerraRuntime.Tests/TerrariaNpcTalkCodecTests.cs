using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaNpcTalkCodecTests
{
    [Theory]
    [InlineData(0, 17)]
    [InlineData(254, 199)]
    [InlineData(42, -1)]
    public void Packet40_roundtrips_exact_1458_payload_shape(byte playerSlot, short npcSlot)
    {
        var state = new TerrariaNpcTalkState(playerSlot, npcSlot);
        Assert.Equal(TerrariaNpcTalkEncodeResult.Encoded, TerrariaNpcTalkCodec.TryEncode(in state, out byte[] encoded));
        Assert.Equal(6, encoded.Length); // 2-byte frame length + packet id + 3-byte payload.
        Assert.Equal((byte)TerrariaMessageId.SetNpcTalk, encoded[2]);

        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(TerrariaNpcTalkDecodeResult.Decoded, TerrariaNpcTalkCodec.TryDecode(in frame, out TerrariaNpcTalkState decoded));
        Assert.Equal(state, decoded);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(200)]
    [InlineData(short.MaxValue)]
    public void Packet40_rejects_out_of_range_npc_slot(short npcSlot)
    {
        var state = new TerrariaNpcTalkState(1, npcSlot);
        Assert.Equal(TerrariaNpcTalkEncodeResult.InvalidState, TerrariaNpcTalkCodec.TryEncode(in state, out byte[] encoded));
        Assert.Empty(encoded);
    }
}
