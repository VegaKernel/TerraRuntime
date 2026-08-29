namespace TerraRuntime.World;

/// <summary>
/// Background-owned shadow of authoritative world tiles for persistence.
/// The game thread hands over immutable section snapshots; this shadow applies only newer revisions and can
/// materialize a complete immutable save image without copying the live world on one simulation tick.
/// </summary>
public sealed class IncrementalWorldTileSaveShadow
{
    private readonly WorldTile[] tiles;
    private readonly long[] sectionRevisions;
    private readonly bool[] initializedSections;
    private int initializedSectionCount;

    public IncrementalWorldTileSaveShadow(WorldDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);

        long tileCount = (long)dimensions.WidthTiles * dimensions.HeightTiles;
        if (tileCount > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "World contains too many tiles for a save shadow.");

        Dimensions = dimensions;
        tiles = GC.AllocateUninitializedArray<WorldTile>((int)tileCount);
        sectionRevisions = new long[dimensions.SectionCount];
        initializedSections = new bool[dimensions.SectionCount];
    }

    public WorldDimensions Dimensions { get; }

    public int InitializedSectionCount => initializedSectionCount;

    public bool IsComplete => initializedSectionCount == Dimensions.SectionCount;

    /// <summary>
    /// Applies one immutable section image. Equal or older revisions are ignored so delayed background work cannot
    /// roll the persistence shadow backwards. The snapshot bounds must exactly match this world's section geometry.
    /// </summary>
    public bool TryApply(WorldSectionTileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        int sectionIndex = TerrariaSectionGeometry.ToLinearIndex(Dimensions, snapshot.Section);
        WorldTileBounds expectedBounds = TerrariaSectionGeometry.GetBounds(Dimensions, snapshot.Section);
        if (snapshot.Bounds != expectedBounds)
        {
            throw new ArgumentException(
                "Section snapshot bounds do not match the save shadow world geometry.",
                nameof(snapshot));
        }

        if (initializedSections[sectionIndex] && snapshot.Revision <= sectionRevisions[sectionIndex])
            return false;

        ReadOnlySpan<WorldTile> source = snapshot.Tiles.Span;
        int sourceIndex = 0;
        for (int y = expectedBounds.Y; y < expectedBounds.ExclusiveBottom; y++)
        {
            for (int x = expectedBounds.X; x < expectedBounds.ExclusiveRight; x++)
            {
                tiles[(x * Dimensions.HeightTiles) + y] = source[sourceIndex++];
            }
        }

        sectionRevisions[sectionIndex] = snapshot.Revision;
        if (!initializedSections[sectionIndex])
        {
            initializedSections[sectionIndex] = true;
            initializedSectionCount++;
        }

        return true;
    }

    public int Apply(ReadOnlySpan<WorldSectionTileSnapshot?> snapshots)
    {
        int applied = 0;
        foreach (WorldSectionTileSnapshot? snapshot in snapshots)
        {
            if (snapshot is not null && TryApply(snapshot))
                applied++;
        }

        return applied;
    }

    /// <summary>
    /// Copies the background shadow into a detached immutable image suitable for an asynchronous serializer.
    /// This is intentionally unavailable until every section has been initialized at least once.
    /// </summary>
    public bool TryCaptureImage(out WorldTileSaveImage? image)
    {
        if (!IsComplete)
        {
            image = null;
            return false;
        }

        image = new WorldTileSaveImage(Dimensions, (WorldTile[])tiles.Clone());
        return true;
    }
}

/// <summary>
/// Detached column-major tile image matching vanilla .wld traversal order.
/// </summary>
public sealed class WorldTileSaveImage
{
    private readonly WorldTile[] tiles;

    internal WorldTileSaveImage(WorldDimensions dimensions, WorldTile[] tiles)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Length != checked(dimensions.WidthTiles * dimensions.HeightTiles))
            throw new ArgumentException("Save image tile count does not match its dimensions.", nameof(tiles));

        Dimensions = dimensions;
        this.tiles = tiles;
    }

    public WorldDimensions Dimensions { get; }

    public int Count => tiles.Length;

    public ReadOnlyMemory<WorldTile> ColumnMajorTiles => tiles;

    public WorldTile Get(int x, int y)
    {
        if ((uint)x >= (uint)Dimensions.WidthTiles)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Dimensions.HeightTiles)
            throw new ArgumentOutOfRangeException(nameof(y));

        return tiles[(x * Dimensions.HeightTiles) + y];
    }
}
