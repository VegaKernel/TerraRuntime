using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaNpcCatchCodecTests
{
    [Fact]
    public void Packet_70_decodes_exact_int16_slot()
    {
        byte[] bytes = [5, 0, (byte)TerrariaMessageId.CatchNpc, 123, 0];
        var buffer = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(TerrariaNpcCatchDecodeResult.Decoded, TerrariaNpcCatchCodec.TryDecode(in frame, out TerrariaNpcCatchState state));
        Assert.Equal((short)123, state.NpcSlot);
    }

    [Fact]
    public void Packet_70_rejects_wrong_payload_length_and_slot_bounds()
    {
        var payload = new ReadOnlySequence<byte>(new byte[] { 1 });
        var frame = new TerrariaFrame(4, (byte)TerrariaMessageId.CatchNpc, payload, payload);
        Assert.Equal(TerrariaNpcCatchDecodeResult.InvalidPayloadLength, TerrariaNpcCatchCodec.TryDecode(in frame, out _));
        Assert.True(TerrariaNpcCatchCodec.IsValidNpcSlot(199));
        Assert.False(TerrariaNpcCatchCodec.IsValidNpcSlot(200));
        Assert.False(TerrariaNpcCatchCodec.IsValidNpcSlot(-1));
    }
}
