namespace TerraRuntime.World;

public enum WorldFileEnvelopeEncodeResult : byte
{
    Encoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionCount = 2,
    InvalidSectionPointers = 3,
    InvalidFrameImportance = 4,
    DestinationNotWritable = 5,
    WriteFailed = 6
}

/// <summary>
/// Encodes the modern Terraria 1.4.5.8 .wld envelope consumed by <see cref="WorldFileEnvelopeParser"/>.
/// Saving is intentionally current-version-only so TerraRuntime never guesses how to emit an unknown future format.
/// </summary>
public static class WorldFileEnvelopeEncoder
{
    private const int FixedPrefixLength = 4 + 7 + 1 + 4 + 8 + 2;
    private const byte WorldFileType = 2;
    private static ReadOnlySpan<byte> Magic => "relogic"u8;

    public static int CurrentEncodedLength =>
        checked(
            FixedPrefixLength +
            (VanillaWorldFormat326.SectionCount * sizeof(int)) +
            sizeof(ushort) +
            ((VanillaWorldFormat326.TileTypeCount + 7) >> 3));

    public static WorldFileEnvelopeEncodeResult TryEncode(
        WorldFileEnvelope source,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileEnvelopeEncodeResult.DestinationNotWritable;
        if (source.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileEnvelopeEncodeResult.UnsupportedVersion;
        if (source.SectionOffsets.Count != VanillaWorldFormat326.SectionCount)
            return WorldFileEnvelopeEncodeResult.InvalidSectionCount;
        if (!HasCanonicalSectionPointers(source.SectionOffsets))
            return WorldFileEnvelopeEncodeResult.InvalidSectionPointers;

        int expectedImportanceBytes = (VanillaWorldFormat326.TileTypeCount + 7) >> 3;
        if (source.FrameImportanceCount != VanillaWorldFormat326.TileTypeCount ||
            source.FrameImportanceBits.Length != expectedImportanceBytes)
        {
            return WorldFileEnvelopeEncodeResult.InvalidFrameImportance;
        }

        try
        {
            using var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(source.FormatVersion);
            writer.Write(Magic);
            writer.Write(WorldFileType);
            writer.Write(source.Revision);
            writer.Write(source.FavoriteFlags);
            writer.Write(checked((short)source.SectionOffsets.Count));
            for (int i = 0; i < source.SectionOffsets.Count; i++)
                writer.Write(source.SectionOffsets[i]);
            writer.Write(checked((ushort)source.FrameImportanceCount));
            writer.Write(source.FrameImportanceBits.Span);
            writer.Flush();
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileEnvelopeEncodeResult.WriteFailed;
        }

        bytesWritten = CurrentEncodedLength;
        return WorldFileEnvelopeEncodeResult.Encoded;
    }

    private static bool HasCanonicalSectionPointers(IReadOnlyList<int> pointers)
    {
        if (pointers.Count == 0 || pointers[0] != CurrentEncodedLength)
            return false;

        int previous = pointers[0];
        for (int i = 1; i < pointers.Count; i++)
        {
            int current = pointers[i];
            if (current <= previous)
                return false;
            previous = current;
        }

        return true;
    }
}
