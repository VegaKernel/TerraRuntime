using System.Buffers;
using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Bridges Multiplicity's owned packet models to TerraRuntime's <see cref="IBufferWriter{Byte}"/> path without
/// staging through <see cref="MemoryStream"/>. The helper also verifies that the model's declared payload length
/// matches the bytes it actually serialized before a frame leaves the protocol boundary.
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

        var writer = new ArrayBufferWriter<byte>(frameLength);
        using var stream = new ArrayBufferWriterStream(writer);
        packet.ToStream(stream);
        if (writer.WrittenCount != frameLength)
        {
            frame = [];
            return false;
        }

        frame = writer.WrittenSpan.ToArray();
        return true;
    }
}
