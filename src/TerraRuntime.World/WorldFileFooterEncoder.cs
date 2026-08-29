using System.Text;

namespace TerraRuntime.World;

public enum WorldFileFooterEncodeResult : byte
{
    Encoded = 0,
    InvalidWorldName = 1,
    WorldNameTooLarge = 2,
    DestinationNotWritable = 3,
    WriteFailed = 4
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 footer: true marker, canonical world name and world id.
/// </summary>
public static class WorldFileFooterEncoder
{
    private const int MaximumNameBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileFooterEncodeResult TryEncode(
        WorldFileHeader header,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileFooterEncodeResult.DestinationNotWritable;
        if (header.Name is null)
            return WorldFileFooterEncodeResult.InvalidWorldName;

        int nameBytes;
        try
        {
            nameBytes = StrictUtf8.GetByteCount(header.Name);
        }
        catch (EncoderFallbackException)
        {
            return WorldFileFooterEncodeResult.InvalidWorldName;
        }

        if (nameBytes > MaximumNameBytes)
            return WorldFileFooterEncodeResult.WorldNameTooLarge;

        long encodedLength = checked(
            sizeof(byte) + Get7BitEncodedIntLength(nameBytes) + nameBytes + sizeof(int));
        try
        {
            using var writer = new BinaryWriter(destination, StrictUtf8, leaveOpen: true);
            writer.Write((byte)1);
            writer.Write(header.Name);
            writer.Write(header.WorldId);
            writer.Flush();
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileFooterEncodeResult.WriteFailed;
        }

        bytesWritten = encodedLength;
        return WorldFileFooterEncodeResult.Encoded;
    }

    private static int Get7BitEncodedIntLength(int value)
    {
        int length = 1;
        while ((uint)value >= 0x80)
        {
            value >>= 7;
            length++;
        }
        return length;
    }
}
