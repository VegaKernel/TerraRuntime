using System.Buffers.Binary;
using System.Text;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaTownNpcIdentityCodecTests
{
    [Fact]
    public void Encodes_source_packet_56_layout()
    {
        var state = new TerrariaTownNpcIdentityState(7, "Andrew", 1);

        Assert.Equal(
            TerrariaTownNpcIdentityEncodeResult.Encoded,
            TerrariaTownNpcIdentityCodec.TryEncode(in state, out byte[] frame));

        Assert.Equal(frame.Length, BinaryPrimitives.ReadUInt16LittleEndian(frame));
        Assert.Equal((byte)TerrariaMessageId.UniqueTownNpcInfoSyncRequest, frame[2]);
        using var reader = new BinaryReader(new MemoryStream(frame, 3, frame.Length - 3), Encoding.UTF8);
        Assert.Equal((short)7, reader.ReadInt16());
        Assert.Equal("Andrew", reader.ReadString());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
    }

    [Fact]
    public void Rejects_out_of_range_npc_slot()
    {
        var state = new TerrariaTownNpcIdentityState(200, string.Empty, 0);
        Assert.Equal(
            TerrariaTownNpcIdentityEncodeResult.InvalidNpcSlot,
            TerrariaTownNpcIdentityCodec.TryEncode(in state, out _));
    }
}
