namespace TerraRuntime.World;

/// <summary>
/// Successfully loaded authoritative core world state. Additional .wld sections such as chests, signs,
/// NPCs and tile entities are intentionally outside this type until their loaders are implemented.
/// </summary>
public sealed record WorldFileCore(
    WorldFileEnvelope Envelope,
    WorldFileHeader Header,
    WorldTileStore Tiles);

public enum WorldFileCoreLoadResult : byte
{
    Loaded = 0,
    InvalidEnvelope = 1,
    InvalidHeader = 2,
    TileBudgetExceeded = 3,
    TileStorageUnsupported = 4,
    InvalidTiles = 5
}

public readonly record struct WorldFileCoreLoadDiagnostic(
    WorldFileCoreLoadResult Result,
    WorldFileEnvelopeParseResult EnvelopeResult,
    WorldFileHeaderParseResult HeaderResult,
    WorldFileTileDecodeResult TileResult);
