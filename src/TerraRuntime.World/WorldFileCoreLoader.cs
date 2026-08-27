namespace TerraRuntime.World;

/// <summary>
/// Loads only the verified core of a Terraria 1.4.5.8 world: envelope, leading header fields and tiles.
/// The result is published only after the complete tile section has decoded successfully.
/// </summary>
public static class WorldFileCoreLoader
{
    public static WorldFileCoreLoadDiagnostic TryLoad(
        ReadOnlySpan<byte> file,
        long maxTileCount,
        out WorldFileCore? world)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTileCount, 1);
        world = null;

        WorldFileEnvelopeParseResult envelopeResult = WorldFileEnvelopeParser.TryParse(
            file,
            out WorldFileEnvelope? envelope,
            out _);
        if (envelopeResult != WorldFileEnvelopeParseResult.Parsed || envelope is null)
        {
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.InvalidEnvelope,
                envelopeResult,
                null,
                null);
        }

        WorldFileHeaderParseResult headerResult = WorldFileHeaderParser.TryParse(file, envelope, out WorldFileHeader? header);
        if (headerResult != WorldFileHeaderParseResult.Parsed || header is null)
        {
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.InvalidHeader,
                envelopeResult,
                headerResult,
                null);
        }

        long tileCount = (long)header.Dimensions.WidthTiles * header.Dimensions.HeightTiles;
        if (tileCount > maxTileCount)
        {
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.TileBudgetExceeded,
                envelopeResult,
                headerResult,
                null);
        }

        WorldTileStore tiles;
        try
        {
            tiles = new WorldTileStore(header.Dimensions);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.TileStorageUnsupported,
                envelopeResult,
                headerResult,
                null);
        }

        WorldFileTileDecodeResult tileResult = WorldFileTileDecoder.TryDecode(
            file,
            envelope,
            header,
            tiles,
            out _);
        if (tileResult != WorldFileTileDecodeResult.Decoded)
        {
            // 'tiles' is intentionally not exposed: a partially decoded world can never become authoritative state.
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.InvalidTiles,
                envelopeResult,
                headerResult,
                tileResult);
        }

        world = new WorldFileCore(envelope, header, tiles);
        return new WorldFileCoreLoadDiagnostic(
            WorldFileCoreLoadResult.Loaded,
            envelopeResult,
            headerResult,
            tileResult);
    }
}
