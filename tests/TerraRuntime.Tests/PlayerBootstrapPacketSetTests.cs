using System.Buffers.Binary;
using TerraRuntime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class PlayerBootstrapPacketSetTests
{
    [Fact]
    public void Testing_packet_set_includes_status_and_tile_frame_packets()
    {
        ReadOnlyMemory<byte>[] sections =
        [
            new byte[] { 3, 0, (byte)TerrariaMessageId.TileSection },
            new byte[] { 3, 0, (byte)TerrariaMessageId.TileSection }
        ];

        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.CreateForTesting(
            worldInfoFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
            baseSectionFrames: sections,
            enterWorldFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf });

        Assert.Equal((byte)TerrariaMessageId.StatusTextSize, packets.StatusFrame.Span[2]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(packets.StatusFrame.Span[3..7]));

        Assert.Equal((byte)TerrariaMessageId.TileFrameSection, packets.BaseTileFrameFrame.Span[2]);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(packets.BaseTileFrameFrame.Span[3..5]));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(packets.BaseTileFrameFrame.Span[5..7]));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(packets.BaseTileFrameFrame.Span[7..9]));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(packets.BaseTileFrameFrame.Span[9..11]));
    }
}
