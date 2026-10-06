using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Original 1.4.5.8 NetMessage.SendData(98): one signed Int16 progression-event identity.</summary>
public static class TerrariaProgressionEventCodec1458
{
    public static byte[] Encode(short eventId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(eventId);
        byte[] frame = new byte[5];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, 5);
        frame[2] = (byte)TerrariaMessageId.NotifyProgressionEvent;
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(3), eventId);
        return frame;
    }
}
