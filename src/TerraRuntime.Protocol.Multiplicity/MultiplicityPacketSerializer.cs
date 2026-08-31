using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Serializes Multiplicity-owned packet models directly into their exact final frame array. The packet model's
/// declared payload length is treated as a contract: under-write and over-write both fail closed before any bytes
/// are published outside the protocol boundary.
/// </summary>
internal static class MultiplicityPacketSerializer
{
    public static byte[] Serialize(TerrariaPacket packet)
    {
        if (!TrySerialize(packet, out byte[] frame))
            throw new InvalidOperationException("Multiplicity packet produced an invalid Terraria frame length.");

        return frame;
    }

    public static bool TrySerialize(TerrariaPacket packet, out byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(packet);

        int payloadLength = packet.GetLength();
        int frameLength = payloadLength + TerrariaPacket.PacketHeaderLength;
        if (payloadLength < 0 || frameLength < TerrariaPacket.PacketHeaderLength || frameLength > short.MaxValue)
        {
            frame = [];
            return false;
        }

        // The array is returned only after ToStream has written exactly every declared byte. If the model
        // under-writes, the uninitialized tail is discarded; if it over-writes, FixedBufferWriteStream records
        // overflow without allocating a growable staging buffer and the candidate is discarded as well.
        byte[] candidate = GC.AllocateUninitializedArray<byte>(frameLength);
        using var stream = new FixedBufferWriteStream(candidate);
        packet.ToStream(stream);
        if (stream.Overflowed || stream.WrittenCount != frameLength)
        {
            frame = [];
            return false;
        }

        frame = candidate;
        return true;
    }
}
