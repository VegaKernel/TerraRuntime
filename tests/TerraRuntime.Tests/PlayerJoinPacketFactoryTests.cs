using global::Multiplicity.Packets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PlayerJoinPacketFactoryTests
{
    [Fact]
    public void ContinueConnecting_encodes_vanilla_slot_assignment_payload()
    {
        ContinueConnecting packet = PlayerJoinPacketFactory.CreateContinueConnecting(
            new PlayerSlotId(17),
            serverSpecialFlag2: true);

        using var stream = new MemoryStream();
        packet.ToStream(stream);
        byte[] bytes = stream.ToArray();

        Assert.Equal(new byte[] { 5, 0, (byte)PacketTypes.ContinueConnecting, 17, 1 }, bytes);
    }
}
