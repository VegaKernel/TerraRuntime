using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class MultiplicityProtocolTests
{
    [Fact]
    public void Packet_header_is_read_without_materializing_a_packet_object()
    {
        byte[] packet = [3, 0, 1];

        Assert.True(MultiplicityPacketInspector.TryReadHeader(packet, out PacketHeaderInfo header));
        Assert.Equal((ushort)3, header.PacketLength);
        Assert.Equal((byte)1, header.MessageId);
        Assert.Equal(0, header.PayloadLength);
        Assert.True(MultiplicityPacketInspector.IsCompletePacket(packet));
    }
}
