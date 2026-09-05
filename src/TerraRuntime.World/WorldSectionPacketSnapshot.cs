namespace TerraRuntime.World;

/// <summary>
/// Immutable world-format rules required to encode network section tiles. A runtime pipeline captures one
/// context per loaded world and shares it across section snapshots instead of giving workers the live world.
/// </summary>
public sealed class WorldSectionEncodingContext
{
    private readonly byte[] _frameImportanceBits;

    private WorldSectionEncodingContext(
        int formatVersion,
        WorldDimensions dimensions,
        int frameImportanceCount,
        byte[] frameImportanceBits)
    {
        FormatVersion = formatVersion;
        Dimensions = dimensions;
        FrameImportanceCount = frameImportanceCount;
        _frameImportanceBits = frameImportanceBits;
    }

    public int FormatVersion { get; }

    public WorldDimensions Dimensions { get; }

    public int FrameImportanceCount { get; }

    public static WorldSectionEncodingContext Capture(WorldFileData world)
    {
        ArgumentNullException.ThrowIfNull(world);
        byte[] frameImportanceBits = world.Envelope.FrameImportanceBits.ToArray();
        return new WorldSectionEncodingContext(
            world.Envelope.FormatVersion,
            world.Header.Dimensions,
            world.Envelope.FrameImportanceCount,
            frameImportanceBits);
    }

    public bool IsFrameImportant(int tileType)
    {
        if ((uint)tileType >= (uint)FrameImportanceCount)
            return false;

        return (_frameImportanceBits[tileType >> 3] & (1 << (tileType & 7))) != 0;
    }
}

public enum WorldSectionPacketSnapshotCaptureResult : byte
{
    Captured = 0,
    IncompatibleContext = 1,
    StaleTileSnapshot = 2,
    InvalidObjectMetadata = 3
}

/// <summary>
/// Complete immutable worker input for rebuilding one Terraria packet 10. Tiles and their revision are copied
/// separately; section-local chest/sign/tile-entity bytes are serialized on the authoritative thread before the
/// snapshot crosses the worker boundary.
/// </summary>
public sealed class WorldSectionPacketSnapshot
{
    internal WorldSectionPacketSnapshot(
        WorldSectionTileSnapshot tiles,
        WorldSectionEncodingContext encodingContext,
        byte[] objectMetadata)
    {
        Tiles = tiles;
        EncodingContext = encodingContext;
        ObjectMetadata = objectMetadata;
    }

    public WorldSectionTileSnapshot Tiles { get; }

    public WorldSectionEncodingContext EncodingContext { get; }

    public ReadOnlyMemory<byte> ObjectMetadata { get; }

    public WorldSectionId Section => Tiles.Section;

    public WorldTileRegion Bounds => Tiles.Bounds;

    public long Revision => Tiles.Revision;
}

public static class WorldSectionPacketSnapshotCapture
{
    /// <summary>
    /// Captures the object tail that belongs to an already stable tile snapshot. Object metadata currently has
    /// no independent mutable runtime revision, so this method must run on the authoritative world thread. Once
    /// runtime chest/sign/tile-entity mutation is introduced, that state must gain its own section revision and
    /// participate in completion validation as well.
    /// </summary>
    public static WorldSectionPacketSnapshotCaptureResult TryCapture(
        WorldFileData world,
        WorldSectionTileSnapshot tiles,
        WorldSectionEncodingContext encodingContext,
        out WorldSectionPacketSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(encodingContext);
        snapshot = null;

        WorldDimensions worldDimensions = world.Header.Dimensions;
        WorldDimensions contextDimensions = encodingContext.Dimensions;
        if (encodingContext.FormatVersion != world.Envelope.FormatVersion ||
            contextDimensions.WidthTiles != worldDimensions.WidthTiles ||
            contextDimensions.HeightTiles != worldDimensions.HeightTiles)
        {
            return WorldSectionPacketSnapshotCaptureResult.IncompatibleContext;
        }

        long before = world.Tiles.GetSectionVersion(tiles.Section);
        if (before != tiles.Revision || (before & 1L) != 0)
            return WorldSectionPacketSnapshotCaptureResult.StaleTileSnapshot;

        WorldTileRegion bounds = tiles.Bounds;
        WorldSectionObjectMetadataEncodeResult metadataResult = WorldSectionObjectMetadataEncoder.TryEncode(
            world,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            out byte[] metadata);
        if (metadataResult != WorldSectionObjectMetadataEncodeResult.Encoded)
            return WorldSectionPacketSnapshotCaptureResult.InvalidObjectMetadata;

        long after = world.Tiles.GetSectionVersion(tiles.Section);
        if (after != tiles.Revision || (after & 1L) != 0)
            return WorldSectionPacketSnapshotCaptureResult.StaleTileSnapshot;

        snapshot = new WorldSectionPacketSnapshot(tiles, encodingContext, metadata);
        return WorldSectionPacketSnapshotCaptureResult.Captured;
    }
}
