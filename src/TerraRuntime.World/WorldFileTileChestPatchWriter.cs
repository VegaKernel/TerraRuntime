namespace TerraRuntime.World;

public enum WorldFileTileChestPatchWriteResult : byte
{
    Written = 0,
    InvalidSourceEnvelope = 1,
    UnsupportedSourceVersion = 2,
    InvalidSourceHeader = 3,
    DimensionMismatch = 4,
    DestinationNotWritable = 5,
    DestinationNotSeekable = 6,
    DestinationNotEmpty = 7,
    InvalidSectionLayout = 8,
    TileEncodeFailed = 9,
    ChestEncodeFailed = 10,
    EnvelopeEncodeFailed = 11,
    FileTooLarge = 12,
    WriteFailed = 13
}

/// <summary>
/// Writes a deliberately partial current-format world save by replacing only the authoritative tile and chest
/// sections of an existing Terraria 1.4.5.8 .wld. The source header and every section from signs onward are
/// preserved byte-for-byte; section pointers are rebuilt around the newly encoded tile/chest payloads.
///
/// This is a compatibility bridge, not a complete world serializer. Callers must provide a fresh seekable
/// destination and discard it on any non-success result.
/// </summary>
public static class WorldFileTileChestPatchWriter
{
    public static WorldFileTileChestPatchWriteResult TryWrite(
        ReadOnlySpan<byte> sourceFile,
        WorldTileSaveImage tiles,
        ReadOnlySpan<WorldChest> chests,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileTileChestPatchWriteResult.DestinationNotWritable;
        if (!destination.CanSeek)
            return WorldFileTileChestPatchWriteResult.DestinationNotSeekable;

        try
        {
            if (destination.Position != 0 || destination.Length != 0)
                return WorldFileTileChestPatchWriteResult.DestinationNotEmpty;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            return WorldFileTileChestPatchWriteResult.WriteFailed;
        }

        WorldFileEnvelopeParseResult envelopeResult = WorldFileEnvelopeParser.TryParse(
            sourceFile,
            out WorldFileEnvelope? sourceEnvelope,
            out int envelopeLength);
        if (envelopeResult != WorldFileEnvelopeParseResult.Parsed || sourceEnvelope is null)
            return WorldFileTileChestPatchWriteResult.InvalidSourceEnvelope;
        if (sourceEnvelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileTileChestPatchWriteResult.UnsupportedSourceVersion;
        if (sourceEnvelope.SectionOffsets.Count != VanillaWorldFormat326.SectionCount ||
            sourceEnvelope.SectionOffsets[0] != envelopeLength)
        {
            return WorldFileTileChestPatchWriteResult.InvalidSectionLayout;
        }

        WorldFileHeaderParseResult headerResult = WorldFileHeaderParser.TryParse(
            sourceFile,
            sourceEnvelope,
            out WorldFileHeader? sourceHeader);
        if (headerResult != WorldFileHeaderParseResult.Parsed || sourceHeader is null)
            return WorldFileTileChestPatchWriteResult.InvalidSourceHeader;

        if (sourceHeader.Dimensions.WidthTiles != tiles.Dimensions.WidthTiles ||
            sourceHeader.Dimensions.HeightTiles != tiles.Dimensions.HeightTiles)
        {
            return WorldFileTileChestPatchWriteResult.DimensionMismatch;
        }

        int sourceHeaderStart = sourceEnvelope.SectionOffsets[0];
        int sourceTileStart = sourceEnvelope.SectionOffsets[1];
        int sourceTailStart = sourceEnvelope.SectionOffsets[3];
        if (sourceHeaderStart != envelopeLength ||
            sourceTileStart <= sourceHeaderStart ||
            sourceTailStart <= sourceEnvelope.SectionOffsets[2] ||
            sourceTailStart > sourceFile.Length)
        {
            return WorldFileTileChestPatchWriteResult.InvalidSectionLayout;
        }

        var sectionOffsets = new int[VanillaWorldFormat326.SectionCount];
        try
        {
            // Reserve the exact current envelope size. It is rewritten with the final section pointers below.
            destination.Write(sourceFile[..envelopeLength]);
            sectionOffsets[0] = envelopeLength;

            destination.Write(sourceFile.Slice(sourceHeaderStart, sourceTileStart - sourceHeaderStart));
            if (!TryGetSectionOffset(destination, out sectionOffsets[1]))
                return WorldFileTileChestPatchWriteResult.FileTooLarge;

            WorldFileTileEncodeResult tileResult = WorldFileTileEncoder.TryEncode(
                tiles,
                sourceEnvelope.FrameImportanceCount,
                sourceEnvelope.FrameImportanceBits.Span,
                destination,
                out _);
            if (tileResult != WorldFileTileEncodeResult.Encoded)
                return WorldFileTileChestPatchWriteResult.TileEncodeFailed;
            if (!TryGetSectionOffset(destination, out sectionOffsets[2]))
                return WorldFileTileChestPatchWriteResult.FileTooLarge;

            WorldFileChestEncodeResult chestResult = WorldFileChestEncoder.TryEncode(
                chests,
                sourceHeader.Dimensions,
                destination,
                out _);
            if (chestResult != WorldFileChestEncodeResult.Encoded)
                return WorldFileTileChestPatchWriteResult.ChestEncodeFailed;
            if (!TryGetSectionOffset(destination, out sectionOffsets[3]))
                return WorldFileTileChestPatchWriteResult.FileTooLarge;

            int preservedTailShift = checked(sectionOffsets[3] - sourceTailStart);
            for (int section = 4; section < sectionOffsets.Length; section++)
                sectionOffsets[section] = checked(sourceEnvelope.SectionOffsets[section] + preservedTailShift);

            destination.Write(sourceFile[sourceTailStart..]);
            long outputLength = destination.Position;
            if (outputLength > int.MaxValue)
                return WorldFileTileChestPatchWriteResult.FileTooLarge;

            var outputEnvelope = new WorldFileEnvelope(
                sourceEnvelope.FormatVersion,
                sourceEnvelope.Revision,
                sourceEnvelope.FavoriteFlags,
                sectionOffsets,
                sourceEnvelope.FrameImportanceCount,
                sourceEnvelope.FrameImportanceBits.ToArray());

            destination.Position = 0;
            WorldFileEnvelopeEncodeResult outputEnvelopeResult = WorldFileEnvelopeEncoder.TryEncode(
                outputEnvelope,
                destination,
                out long encodedEnvelopeLength);
            if (outputEnvelopeResult != WorldFileEnvelopeEncodeResult.Encoded || encodedEnvelopeLength != envelopeLength)
                return WorldFileTileChestPatchWriteResult.EnvelopeEncodeFailed;

            destination.Position = outputLength;
            bytesWritten = outputLength;
            return WorldFileTileChestPatchWriteResult.Written;
        }
        catch (OverflowException)
        {
            bytesWritten = 0;
            return WorldFileTileChestPatchWriteResult.FileTooLarge;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileTileChestPatchWriteResult.WriteFailed;
        }
    }

    private static bool TryGetSectionOffset(Stream destination, out int offset)
    {
        long position = destination.Position;
        if (position > int.MaxValue)
        {
            offset = 0;
            return false;
        }

        offset = checked((int)position);
        return true;
    }
}
