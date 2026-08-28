using System.Buffers.Binary;
using TerraRuntime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class PlayerBootstrapPacketSetTests
{
    [Fact]
    public void Testing_packet_set_includes_status_packet_for_base_sections()
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
    }
}
