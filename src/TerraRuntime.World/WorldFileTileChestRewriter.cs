namespace TerraRuntime.World;

public enum WorldFileTileChestRewriteResult : byte
{
    Rewritten = 0,
    UnsupportedVersion = 1,
    InvalidSectionCount = 2,
    InvalidTileDimensions = 3,
    DestinationNotWritable = 4,
    DestinationNotSeekable = 5,
    DestinationNotEmpty = 6,
    SectionOffsetOverflow = 7,
    TileEncodingFailed = 8,
    ChestEncodingFailed = 9,
    EnvelopeEncodingFailed = 10,
    FooterEncodingFailed = 11,
    WriteFailed = 12,
    InvalidHeaderSection = 13,
    InvalidSignSection = 14
}

/// <summary>
/// Rebuilds the current authoritative tile/chest persistence slice into a complete Terraria 1.4.5.8 .wld file.
/// The validated source header and all currently non-authoritative sections are preserved byte-for-byte; tiles and
/// chests are encoded from detached runtime state. This deliberately is not a general world writer: subsystems that
/// are not represented by the supplied snapshot remain exactly as they were in the canonical source file.
/// </summary>
public static class WorldFileTileChestRewriter
{
    public static WorldFileTileChestRewriteResult TryRewrite(
        WorldFileEnvelope sourceEnvelope,
        WorldFileHeader header,
        WorldFilePreservedSections preserved,
        WorldTileSaveImage tiles,
        ReadOnlySpan<WorldChest> chests,
        Stream destination,
        out long bytesWritten) =>
        TryRewrite(
            sourceEnvelope,
            header,
            preserved.Header.Span,
            preserved,
            tiles,
            chests,
            destination,
            out bytesWritten);

    /// <summary>
    /// Rewrites using a same-length validated replacement for the opaque preserved header. This lets callers patch
    /// runtime-owned header fields such as the world clock without cloning the other preserved sections or exposing
    /// unmodelled vanilla save flags to a lossy semantic encoder.
    /// </summary>
    public static WorldFileTileChestRewriteResult TryRewrite(
        WorldFileEnvelope sourceEnvelope,
        WorldFileHeader header,
        ReadOnlySpan<byte> headerSection,
        WorldFilePreservedSections preserved,
        WorldTileSaveImage tiles,
        ReadOnlySpan<WorldChest> chests,
        Stream destination,
        out long bytesWritten) =>
        TryRewrite(
            sourceEnvelope,
            header,
            headerSection,
            preserved,
            tiles,
            chests,
            preserved.Signs.Span,
            destination,
            out bytesWritten);

    /// <summary>
    /// Rewrites using validated header and sign section replacements while preserving every other opaque section.
    /// The sign section must already be encoded in the current world format; semantic validation belongs to the
    /// authoritative sign encoder before this byte-level composition step.
    /// </summary>
    public static WorldFileTileChestRewriteResult TryRewrite(
        WorldFileEnvelope sourceEnvelope,
        WorldFileHeader header,
        ReadOnlySpan<byte> headerSection,
        WorldFilePreservedSections preserved,
        WorldTileSaveImage tiles,
        ReadOnlySpan<WorldChest> chests,
        ReadOnlySpan<byte> signSection,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(sourceEnvelope);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(preserved);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (headerSection.Length != preserved.Header.Length || headerSection.IsEmpty)
            return WorldFileTileChestRewriteResult.InvalidHeaderSection;
        if (signSection.IsEmpty)
            return WorldFileTileChestRewriteResult.InvalidSignSection;
        if (!destination.CanWrite)
            return WorldFileTileChestRewriteResult.DestinationNotWritable;
        if (!destination.CanSeek)
            return WorldFileTileChestRewriteResult.DestinationNotSeekable;
        if (sourceEnvelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileTileChestRewriteResult.UnsupportedVersion;
        if (sourceEnvelope.SectionOffsets.Count != VanillaWorldFormat326.SectionCount)
            return WorldFileTileChestRewriteResult.InvalidSectionCount;
        if (tiles.Dimensions.WidthTiles != header.Dimensions.WidthTiles ||
            tiles.Dimensions.HeightTiles != header.Dimensions.HeightTiles)
        {
            return WorldFileTileChestRewriteResult.InvalidTileDimensions;
        }

        try
        {
            if (destination.Position != 0 || destination.Length != 0)
                return WorldFileTileChestRewriteResult.DestinationNotEmpty;

            destination.Position = WorldFileEnvelopeEncoder.CurrentEncodedLength;
            int[] offsets = new int[VanillaWorldFormat326.SectionCount];

            if (!TryRecordOffset(destination, offsets, 0))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            destination.Write(headerSection);

            if (!TryRecordOffset(destination, offsets, 1))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            WorldFileTileEncodeResult tileResult = WorldFileTileEncoder.TryEncode(
                tiles,
                sourceEnvelope.FrameImportanceCount,
                sourceEnvelope.FrameImportanceBits.Span,
                destination,
                out _);
            if (tileResult != WorldFileTileEncodeResult.Encoded)
                return WorldFileTileChestRewriteResult.TileEncodingFailed;

            if (!TryRecordOffset(destination, offsets, 2))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            WorldFileChestEncodeResult chestResult = WorldFileChestEncoder.TryEncode(
                chests,
                header.Dimensions,
                destination,
                out _);
            if (chestResult != WorldFileChestEncodeResult.Encoded)
                return WorldFileTileChestRewriteResult.ChestEncodingFailed;

            if (!TryRecordOffset(destination, offsets, 3))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            destination.Write(signSection);
            if (!TryRecordOffset(destination, offsets, 4))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            destination.Write(preserved.Npcs.Span);
            if (!TryRecordOffset(destination, offsets, 5))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            destination.Write(preserved.TileEntities.Span);
            if (!TryRecordOffset(destination, offsets, 6))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            destination.Write(preserved.PressurePlates.Span);
            if (!TryRecordOffset(destination, offsets, 7))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            destination.Write(preserved.TownRooms.Span);
            if (!TryRecordOffset(destination, offsets, 8))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            destination.Write(preserved.Bestiary.Span);
            if (!TryRecordOffset(destination, offsets, 9))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            destination.Write(preserved.CreativePowers.Span);

            if (!TryRecordOffset(destination, offsets, 10))
                return WorldFileTileChestRewriteResult.SectionOffsetOverflow;
            WorldFileFooterEncodeResult footerResult = WorldFileFooterEncoder.TryEncode(
                header,
                destination,
                out _);
            if (footerResult != WorldFileFooterEncodeResult.Encoded)
                return WorldFileTileChestRewriteResult.FooterEncodingFailed;

            long end = destination.Position;
            var rewrittenEnvelope = new WorldFileEnvelope(
                sourceEnvelope.FormatVersion,
                sourceEnvelope.Revision,
                sourceEnvelope.FavoriteFlags,
                offsets,
                sourceEnvelope.FrameImportanceCount,
                sourceEnvelope.FrameImportanceBits.ToArray());

            destination.Position = 0;
            WorldFileEnvelopeEncodeResult envelopeResult = WorldFileEnvelopeEncoder.TryEncode(
                rewrittenEnvelope,
                destination,
                out long envelopeBytes);
            if (envelopeResult != WorldFileEnvelopeEncodeResult.Encoded ||
                envelopeBytes != WorldFileEnvelopeEncoder.CurrentEncodedLength)
            {
                return WorldFileTileChestRewriteResult.EnvelopeEncodingFailed;
            }

            destination.Position = end;
            destination.SetLength(end);
            bytesWritten = end;
            return WorldFileTileChestRewriteResult.Rewritten;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            bytesWritten = 0;
            return WorldFileTileChestRewriteResult.WriteFailed;
        }
    }

    private static bool TryRecordOffset(Stream destination, int[] offsets, int index)
    {
        long position = destination.Position;
        if (position < 0 || position > int.MaxValue)
            return false;

        offsets[index] = (int)position;
        return true;
    }
}
