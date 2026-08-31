namespace TerraRuntime.World;

public static partial class WorldFileLoader
{
    /// <summary>
    /// Validates and reloads a freshly composed world into an unpublished caller-owned tile store instead of
    /// allocating a second full-world tile array. The destination may be partially overwritten on failure and
    /// therefore must never be authoritative or observable by gameplay while this method is running.
    /// </summary>
    internal static WorldFileLoadDiagnostic TryLoadReusingTiles(
        ReadOnlySpan<byte> file,
        WorldFileLoadLimits limits,
        WorldTileStore tiles,
        out WorldFileData? world)
    {
        limits.Validate();
        ArgumentNullException.ThrowIfNull(tiles);
        world = null;

        WorldFileEnvelopeParseResult envelopeResult = WorldFileEnvelopeParser.TryParse(
            file,
            out WorldFileEnvelope? envelope,
            out _);
        if (envelopeResult != WorldFileEnvelopeParseResult.Parsed || envelope is null)
        {
            return Failure(
                WorldFileLoadResult.InvalidEnvelope,
                WorldFileLoadStage.Envelope,
                (int)envelopeResult);
        }

        WorldFileHeaderParseResult headerResult = WorldFileHeaderParser.TryParse(file, envelope, out WorldFileHeader? header);
        if (headerResult != WorldFileHeaderParseResult.Parsed || header is null)
        {
            return Failure(
                WorldFileLoadResult.InvalidHeader,
                WorldFileLoadStage.Header,
                (int)headerResult);
        }

        long tileCount = (long)header.Dimensions.WidthTiles * header.Dimensions.HeightTiles;
        if (tileCount != tiles.Count || tileCount > limits.MaxTileCount)
        {
            return Failure(
                WorldFileLoadResult.InvalidTiles,
                WorldFileLoadStage.Tiles,
                0x200);
        }

        WorldDimensions dimensions = tiles.Dimensions;
        if (dimensions.WidthTiles != header.Dimensions.WidthTiles ||
            dimensions.HeightTiles != header.Dimensions.HeightTiles)
        {
            return Failure(
                WorldFileLoadResult.InvalidTiles,
                WorldFileLoadStage.Tiles,
                0x201);
        }

        WorldFileTileDecodeResult tileResult = WorldFileTileDecoder.TryDecode(
            file,
            envelope,
            header,
            tiles,
            out _);
        if (tileResult != WorldFileTileDecodeResult.Decoded)
        {
            return Failure(
                WorldFileLoadResult.InvalidTiles,
                WorldFileLoadStage.Tiles,
                (int)tileResult);
        }

        var core = new WorldFileCore(envelope, header, tiles);
        return TryLoadPreparedCoreValidated(file, limits, core, out world);
    }
}
