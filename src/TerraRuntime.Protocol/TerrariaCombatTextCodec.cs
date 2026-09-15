using System.Buffers.Binary;
using System.Text;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-326 encoders for numeric (81) and literal string (119) client combat text. Terraria 1.4.5.8 handles this as
/// <c>CombatText.NewText</c> on the client (initial Y velocity -7, then 0.92 velocity damping each update), so the
/// text rises and slows/fades as ordinary world combat text without using the chat channel.
/// </summary>
public static class TerrariaCombatTextCodec
{
    public const int MaximumTextLength = 64;
    private const byte CombatTextStringMessageId = 119;

    /// <summary>Encodes original NetMessage.SendData(81), used by NPC.HealEffect on the dedicated server.</summary>
    public static byte[] EncodeNumber(float x, float y, int amount, TerrariaRgbColor color)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x));

        var frame = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, 18);
        frame[2] = 81;
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(3), x);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(7), y);
        frame[11] = color.R;
        frame[12] = color.G;
        frame[13] = color.B;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(14), amount);
        return frame;
    }

    public static byte[] EncodeString(float x, float y, string text, TerrariaRgbColor color)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaximumTextLength || text.IndexOf('\0') >= 0)
            throw new ArgumentOutOfRangeException(nameof(text));

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        Span<byte> lengthPrefix = stackalloc byte[5];
        int prefixLength = Write7BitEncodedInt(lengthPrefix, utf8.Length);
        int payloadLength = 1 + sizeof(float) * 2 + 3 + 1 + prefixLength + utf8.Length;
        int frameLength = checked(payloadLength + sizeof(ushort));
        var frame = new byte[frameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frameLength));
        int offset = sizeof(ushort);
        frame[offset++] = CombatTextStringMessageId;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(x)); offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(y)); offset += 4;
        frame[offset++] = color.R;
        frame[offset++] = color.G;
        frame[offset++] = color.B;
        frame[offset++] = 0; // NetworkText.Mode.Literal
        lengthPrefix[..prefixLength].CopyTo(frame.AsSpan(offset)); offset += prefixLength;
        utf8.CopyTo(frame.AsSpan(offset));
        return frame;
    }

    private static int Write7BitEncodedInt(Span<byte> destination, int value)
    {
        uint remaining = checked((uint)value);
        int written = 0;
        while (remaining >= 0x80)
        {
            destination[written++] = (byte)((remaining & 0x7F) | 0x80);
            remaining >>= 7;
        }
        destination[written++] = (byte)remaining;
        return written;
    }
}
