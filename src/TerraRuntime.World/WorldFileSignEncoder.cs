using System.Text;

namespace TerraRuntime.World;

public enum WorldFileSignEncodeResult : byte
{
    Encoded = 0,
    TooManySigns = 1,
    NonCanonicalSlotOrder = 2,
    InvalidSignCoordinates = 3,
    DuplicateCoordinates = 4, // retained for API compatibility; vanilla SaveSigns persists duplicates
    InvalidText = 5,
    TextBudgetExceeded = 6,
    DestinationNotWritable = 7,
    WriteFailed = 8
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 .wld sign section from detached authoritative sign state.
/// File order becomes the packet-visible sign slot after load, so sparse/reordered slot identities are rejected
/// instead of being silently renumbered across a restart.
/// </summary>
public static class WorldFileSignEncoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileSignEncodeResult TryEncode(
        ReadOnlySpan<WorldSign> source,
        WorldDimensions dimensions,
        int maxTextBytesPerSign,
        long maxTotalTextBytes,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTextBytesPerSign);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTotalTextBytes);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileSignEncodeResult.DestinationNotWritable;
        if (source.Length > VanillaWorldFormat326.MaximumSignSlots || source.Length > short.MaxValue)
            return WorldFileSignEncodeResult.TooManySigns;

        WorldFileSignEncodeResult validation = Validate(
            source,
            dimensions,
            maxTextBytesPerSign,
            maxTotalTextBytes,
            out long encodedLength);
        if (validation != WorldFileSignEncodeResult.Encoded)
            return validation;

        try
        {
            using var writer = new BinaryWriter(destination, StrictUtf8, leaveOpen: true);
            writer.Write(checked((short)source.Length));
            foreach (WorldSign sign in source)
            {
                writer.Write(sign.Text);
                writer.Write(sign.X);
                writer.Write(sign.Y);
            }
            writer.Flush();
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileSignEncodeResult.WriteFailed;
        }

        bytesWritten = encodedLength;
        return WorldFileSignEncodeResult.Encoded;
    }

    private static WorldFileSignEncodeResult Validate(
        ReadOnlySpan<WorldSign> source,
        WorldDimensions dimensions,
        int maxTextBytesPerSign,
        long maxTotalTextBytes,
        out long encodedLength)
    {
        encodedLength = sizeof(short);
        long totalTextBytes = 0;
        for (int index = 0; index < source.Length; index++)
        {
            WorldSign sign = source[index];
            if (sign is null)
                return WorldFileSignEncodeResult.InvalidText;
            if (sign.SlotId != index)
                return WorldFileSignEncodeResult.NonCanonicalSlotOrder;
            if ((uint)sign.X >= (uint)dimensions.WidthTiles ||
                (uint)sign.Y >= (uint)dimensions.HeightTiles)
            {
                return WorldFileSignEncodeResult.InvalidSignCoordinates;
            }

            // Vanilla 1.4.5.8 SaveSigns writes every non-null slot in ascending runtime slot order,
            // including duplicate coordinates. LoadSigns removes later duplicates after the next restart.
            if (sign.Text is null)
                return WorldFileSignEncodeResult.InvalidText;

            int textBytes;
            try
            {
                textBytes = StrictUtf8.GetByteCount(sign.Text);
            }
            catch (EncoderFallbackException)
            {
                return WorldFileSignEncodeResult.InvalidText;
            }

            if (textBytes > maxTextBytesPerSign ||
                textBytes > maxTotalTextBytes - totalTextBytes)
            {
                return WorldFileSignEncodeResult.TextBudgetExceeded;
            }
            totalTextBytes += textBytes;

            encodedLength = checked(
                encodedLength +
                Get7BitEncodedIntLength(textBytes) + textBytes +
                sizeof(int) + sizeof(int));
        }

        return WorldFileSignEncodeResult.Encoded;
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
