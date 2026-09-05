using Multiplicity.Packets.Views;

namespace TerraRuntime.Protocol.Multiplicity;

public static class PacketInspector
{
    public static bool TryReadHeader(ReadOnlySpan<byte> buffer, out PacketHeaderInfo header)
    {
        if (!PacketViewParser.TryReadHeader(buffer, out PacketHeader parsed))
        {
            header = default;
            return false;
        }

        header = new PacketHeaderInfo(parsed.PacketLength, parsed.RawPacketId, parsed.PayloadLength);
        return true;
    }

    public static bool IsCompletePacket(ReadOnlySpan<byte> buffer)
    {
        return PacketViewParser.TryParseExact(buffer, out _);
    }
}
