using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaNpcHomeCodecTests
{
    [Fact]
    public void Packet60_roundtrips_exact_1458_payload_shape()
    {
        var state = new TerrariaNpcHomeState(7, 123, 456, (byte)TerrariaNpcHomeStatus.HasRoom);
        Assert.Equal(TerrariaNpcHomeEncodeResult.Encoded, TerrariaNpcHomeCodec.TryEncode(in state, out byte[] encoded));
        Assert.Equal(10, encoded.Length); // 2-byte frame length + packet id + 7-byte payload.
        Assert.Equal((byte)TerrariaMessageId.UpdateNpcHome, encoded[2]);

        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(TerrariaNpcHomeDecodeResult.Decoded, TerrariaNpcHomeCodec.TryDecode(in frame, out TerrariaNpcHomeState decoded));
        Assert.Equal(state, decoded);
    }

    [Fact]
    public void Packet60_rejects_unknown_household_status_on_encode()
    {
        var state = new TerrariaNpcHomeState(1, 10, 20, 3);
        Assert.Equal(TerrariaNpcHomeEncodeResult.InvalidState, TerrariaNpcHomeCodec.TryEncode(in state, out byte[] frame));
        Assert.Empty(frame);
    }
}
