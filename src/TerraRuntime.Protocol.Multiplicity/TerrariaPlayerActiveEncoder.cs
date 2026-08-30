using System.Buffers;
using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Encodes the protocol-326 player lifecycle packet. TerrariaServer 1.4.5.8
/// <c>NetMessage.SyncOnePlayer</c> sends packet 14 before the rest of a player's baseline and sends
/// the inactive form when that player disconnects.
/// </summary>
public static class TerrariaPlayerActiveEncoder
{
    public static byte[] Encode(byte playerId, bool active)
    {
        var packet = new PlayerActive
        {
            PlayerId = playerId,
            Active = active
        };

        var writer = new ArrayBufferWriter<byte>(packet.GetLength() + TerrariaPacket.PacketHeaderLength);
        using var stream = new ArrayBufferWriterStream(writer);
        packet.ToStream(stream);
        return writer.WrittenSpan.ToArray();
    }
}
