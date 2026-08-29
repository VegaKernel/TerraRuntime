using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

public enum WorldFileHeaderPrefixEncodeResult : byte
{
    Encoded = 0,
    InvalidString = 1,
    StringTooLarge = 2,
    InvalidWorldBounds = 3,
    DestinationNotWritable = 4,
    WriteFailed = 5
}

/// <summary>
/// Encodes the identity/dimension prefix of the Terraria 1.4.5.8 world-header section. Runtime metadata follows
/// this prefix in the same section and is intentionally not emitted here.
/// </summary>
public static class WorldFileHeaderPrefixEncoder
{
    private const int MaximumStringBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileHeaderPrefixEncodeResult TryEncode(
        WorldFileHeader header,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileHeaderPrefixEncodeResult.DestinationNotWritable;
        if (header.RightWorld <= header.LeftWorld || header.BottomWorld <= header.TopWorld)
            return WorldFileHeaderPrefixEncodeResult.InvalidWorldBounds;

        byte[] nameBytes;
        byte[] seedBytes;
        try
        {
            nameBytes = StrictUtf8.GetBytes(header.Name);
            seedBytes = StrictUtf8.GetBytes(header.SeedText);
        }
        catch (EncoderFallbackException)
        {
            return WorldFileHeaderPrefixEncodeResult.InvalidString;
        }

        if (nameBytes.Length > MaximumStringBytes || seedBytes.Length > MaximumStringBytes)
            return WorldFileHeaderPrefixEncodeResult.StringTooLarge;

        try
        {
            WriteString(destination, nameBytes);
            WriteString(destination, seedBytes);

            Span<byte> scalar = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(scalar, header.WorldGeneratorVersion);
            destination.Write(scalar);

            Span<byte> guid = stackalloc byte[16];
            if (!header.UniqueId.TryWriteBytes(guid))
                throw new InvalidOperationException("A Guid must fit in 16 bytes.");
            destination.Write(guid);

            Span<byte> integer = stackalloc byte[sizeof(int)];
            WriteInt32(destination, integer, header.WorldId);
            WriteInt32(destination, integer, header.LeftWorld);
            WriteInt32(destination, integer, header.RightWorld);
            WriteInt32(destination, integer, header.TopWorld);
            WriteInt32(destination, integer, header.BottomWorld);
            WriteInt32(destination, integer, header.Dimensions.HeightTiles);
            WriteInt32(destination, integer, header.Dimensions.WidthTiles);
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileHeaderPrefixEncodeResult.WriteFailed;
        }

        bytesWritten = checked(
            Get7BitEncodedLength(nameBytes.Length) + nameBytes.Length +
            Get7BitEncodedLength(seedBytes.Length) + seedBytes.Length +
            sizeof(ulong) + 16L + (7L * sizeof(int)));
        return WorldFileHeaderPrefixEncodeResult.Encoded;
    }

    private static void WriteString(Stream destination, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[5];
        int count = Write7BitEncodedInt(length, bytes.Length);
        destination.Write(length[..count]);
        destination.Write(bytes);
    }

    private static void WriteInt32(Stream destination, Span<byte> buffer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        destination.Write(buffer);
    }

    private static int Write7BitEncodedInt(Span<byte> destination, int value)
    {
        uint remaining = (uint)value;
        int count = 0;
        while (remaining >= 0x80)
        {
            destination[count++] = (byte)(remaining | 0x80);
            remaining >>= 7;
        }

        destination[count++] = (byte)remaining;
        return count;
    }

    private static int Get7BitEncodedLength(int value)
    {
        uint remaining = (uint)value;
        int count = 1;
        while (remaining >= 0x80)
        {
            count++;
            remaining >>= 7;
        }

        return count;
    }
}
