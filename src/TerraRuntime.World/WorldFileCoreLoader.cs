using System.Diagnostics;

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
        out WorldFileCore? world) =>
        TryLoad(file, maxTileCount, out world, out _);

    public static WorldFileCoreLoadDiagnostic TryLoad(
        ReadOnlySpan<byte> file,
        long maxTileCount,
        out WorldFileCore? world,
        out WorldFileLoadProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTileCount, 1);
        world = null;

        long totalStart = Stopwatch.GetTimestamp();
        long stageStart = totalStart;
        TimeSpan envelopeAndHeader = TimeSpan.Zero;
        TimeSpan tileAllocation = TimeSpan.Zero;
        TimeSpan tileDecode = TimeSpan.Zero;

        WorldFileEnvelopeParseResult envelopeResult = WorldFileEnvelopeParser.TryParse(
            file,
            out WorldFileEnvelope? envelope,
            out _);
        if (envelopeResult != WorldFileEnvelopeParseResult.Parsed || envelope is null)
        {
            envelopeAndHeader = Stopwatch.GetElapsedTime(stageStart);
            profile = new WorldFileLoadProfile(
                envelopeAndHeader,
                tileAllocation,
                tileDecode,
                TimeSpan.Zero,
                Stopwatch.GetElapsedTime(totalStart));
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.InvalidEnvelope,
                envelopeResult,
                null,
                null);
        }

        WorldFileHeaderParseResult headerResult = WorldFileHeaderParser.TryParse(file, envelope, out WorldFileHeader? header);
        envelopeAndHeader = Stopwatch.GetElapsedTime(stageStart);
        if (headerResult != WorldFileHeaderParseResult.Parsed || header is null)
        {
            profile = new WorldFileLoadProfile(
                envelopeAndHeader,
                tileAllocation,
                tileDecode,
                TimeSpan.Zero,
                Stopwatch.GetElapsedTime(totalStart));
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.InvalidHeader,
                envelopeResult,
                headerResult,
                null);
        }

        long tileCount = (long)header.Dimensions.WidthTiles * header.Dimensions.HeightTiles;
        if (tileCount > maxTileCount)
        {
            profile = new WorldFileLoadProfile(
                envelopeAndHeader,
                tileAllocation,
                tileDecode,
                TimeSpan.Zero,
                Stopwatch.GetElapsedTime(totalStart));
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.TileBudgetExceeded,
                envelopeResult,
                headerResult,
                null);
        }

        WorldTileStore tiles;
        stageStart = Stopwatch.GetTimestamp();
        try
        {
            tiles = new WorldTileStore(header.Dimensions);
            tileAllocation = Stopwatch.GetElapsedTime(stageStart);
        }
        catch (ArgumentOutOfRangeException)
        {
            tileAllocation = Stopwatch.GetElapsedTime(stageStart);
            profile = new WorldFileLoadProfile(
                envelopeAndHeader,
                tileAllocation,
                tileDecode,
                TimeSpan.Zero,
                Stopwatch.GetElapsedTime(totalStart));
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.TileStorageUnsupported,
                envelopeResult,
                headerResult,
                null);
        }

        stageStart = Stopwatch.GetTimestamp();
        WorldFileTileDecodeResult tileResult = WorldFileTileDecoder.TryDecode(
            file,
            envelope,
            header,
            tiles,
            out _);
        tileDecode = Stopwatch.GetElapsedTime(stageStart);
        if (tileResult != WorldFileTileDecodeResult.Decoded)
        {
            profile = new WorldFileLoadProfile(
                envelopeAndHeader,
                tileAllocation,
                tileDecode,
                TimeSpan.Zero,
                Stopwatch.GetElapsedTime(totalStart));
            // 'tiles' is intentionally not exposed: a partially decoded world can never become authoritative state.
            return new WorldFileCoreLoadDiagnostic(
                WorldFileCoreLoadResult.InvalidTiles,
                envelopeResult,
                headerResult,
                tileResult);
        }

        world = new WorldFileCore(envelope, header, tiles);
        profile = new WorldFileLoadProfile(
            envelopeAndHeader,
            tileAllocation,
            tileDecode,
            TimeSpan.Zero,
            Stopwatch.GetElapsedTime(totalStart));
        return new WorldFileCoreLoadDiagnostic(
            WorldFileCoreLoadResult.Loaded,
            envelopeResult,
            headerResult,
            tileResult);
    }
}
