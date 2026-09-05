namespace TerraRuntime.World;

/// <summary>
/// Contiguous column-major tile storage: index = x * height + y.
/// This matches vanilla .wld traversal order, keeping load/save scans sequential without dictating packet-section layout.
/// Mutable access is intended for the authoritative game thread.
/// </summary>
public sealed class WorldTileStore
{
    private readonly WorldTile[] _tiles;
    private readonly long[] _sectionVersions;

    public WorldTileStore(WorldDimensions dimensions)
        : this(dimensions, skipZeroInitialization: false)
    {
    }

    private WorldTileStore(WorldDimensions dimensions, bool skipZeroInitialization)
    {
        ArgumentNullException.ThrowIfNull(dimensions);

        long tileCount = (long)dimensions.WidthTiles * dimensions.HeightTiles;
        if (tileCount > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "World contains too many tiles for contiguous storage.");
        }

        Dimensions = dimensions;
        _tiles = skipZeroInitialization
            ? GC.AllocateUninitializedArray<WorldTile>((int)tileCount)
            : new WorldTile[(int)tileCount];
        _sectionVersions = new long[dimensions.SectionCount];
        LiquidUpdates = new WorldLiquidUpdateQueue(dimensions);
        DirtySections = new DirtySectionTracker(dimensions);
        PersistenceDirtySections = new DirtySectionTracker(dimensions);
    }

    /// <summary>
    /// Allocates tile backing storage without paying for a redundant managed zero-fill. This is valid only
    /// for transactional snapshot loading, which overwrites every tile before the store can be published.
    /// </summary>
    internal static WorldTileStore CreateForSnapshotLoad(WorldDimensions dimensions) =>
        new(dimensions, skipZeroInitialization: true);

    public WorldDimensions Dimensions { get; }

    public WorldLiquidUpdateQueue LiquidUpdates { get; }

    /// <summary>
    /// Network-section backlog dirtied through the authoritative tile mutation API. Packet-section rebuild consumers
    /// own and drain this tracker independently from persistence so neither subsystem can steal the other's work.
    /// Canonical world and snapshot loaders write the private backing span directly, so initial publication stays clean.
    /// </summary>
    public DirtySectionTracker DirtySections { get; }

    /// <summary>
    /// Persistence-section backlog for incremental save-shadow synchronization. Every authoritative mutation marks
    /// both this tracker and <see cref="DirtySections"/>; save consumers drain only this tracker.
    /// </summary>
    public DirtySectionTracker PersistenceDirtySections { get; }

    /// <summary>
    /// Validated immutable world-surface geometry from the canonical runtime metadata. It is attached only
    /// after the complete .wld has passed transactional validation, so ad-hoc/test stores may leave it unset.
    /// </summary>
    public double? WorldSurfaceTiles { get; private set; }

    public int Count => _tiles.Length;

    public WorldTile Get(int x, int y) => _tiles[GetIndex(x, y)];

    public void Set(int x, int y, in WorldTile tile)
    {
        int index = GetIndex(x, y);
        WorldSectionId section = TerrariaSectionGeometry.FromTile(Dimensions, x, y);
        int sectionIndex = TerrariaSectionGeometry.ToLinearIndex(Dimensions, section);

        // The odd/even version is a tiny seqlock around the 16-byte struct write. The authoritative game
        // thread remains the only writer, while asynchronous snapshot consumers can detect a concurrent edit.
        Interlocked.Increment(ref _sectionVersions[sectionIndex]);
        _tiles[index] = tile;
        Interlocked.Increment(ref _sectionVersions[sectionIndex]);
        DirtySections.MarkDirty(section);
        PersistenceDirtySections.MarkDirty(section);
    }

    /// <summary>
    /// Writes one tile while constructing an unpublished world candidate. This deliberately bypasses section
    /// revisions and both dirty trackers: no network/save consumer can observe the store before publication, and
    /// treating initial population as live mutation would manufacture a full-world backlog before runtime starts.
    /// Callers must use <see cref="Set"/> after the store becomes authoritative.
    /// </summary>
    internal void SetInitialPopulationTile(int x, int y, in WorldTile tile) =>
        _tiles[GetIndex(x, y)] = tile;

    /// <summary>
    /// Returns the current section version token. Stable sections have an even token; an odd token means a
    /// mutation is currently being committed. Consumers should compare tokens for equality, not interpret them.
    /// </summary>
    public long GetSectionVersion(WorldSectionId section)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(Dimensions, section);
        return Volatile.Read(ref _sectionVersions[index]);
    }

    /// <summary>
    /// Copies one network section into an immutable row-major snapshot without exposing live mutable tiles.
    /// The authoritative thread succeeds immediately. Concurrent readers fail instead of publishing a torn image.
    /// </summary>
    public bool TryCaptureSectionSnapshot(
        WorldSectionId section,
        out WorldSectionTileSnapshot? snapshot)
    {
        int sectionIndex = TerrariaSectionGeometry.ToLinearIndex(Dimensions, section);
        long before = Volatile.Read(ref _sectionVersions[sectionIndex]);
        if ((before & 1L) != 0)
        {
            snapshot = null;
            return false;
        }

        WorldTileRegion bounds = TerrariaSectionGeometry.GetBounds(Dimensions, section);
        var copy = GC.AllocateUninitializedArray<WorldTile>(checked(bounds.Width * bounds.Height));
        int destination = 0;
        for (int y = bounds.Y; y < bounds.ExclusiveBottom; y++)
        {
            for (int x = bounds.X; x < bounds.ExclusiveRight; x++)
            {
                copy[destination++] = _tiles[GetUncheckedIndex(x, y)];
            }
        }

        long after = Volatile.Read(ref _sectionVersions[sectionIndex]);
        if (before != after || (after & 1L) != 0)
        {
            snapshot = null;
            return false;
        }

        snapshot = new WorldSectionTileSnapshot(section, bounds, after, copy);
        return true;
    }

    internal Span<WorldTile> Tiles => _tiles;

    internal WorldTile[] TileArray => _tiles;

    internal int GetUncheckedIndex(int x, int y) => (x * Dimensions.HeightTiles) + y;

    internal bool TryAttachWorldSurface(double worldSurfaceTiles)
    {
        if (!double.IsFinite(worldSurfaceTiles) ||
            worldSurfaceTiles <= 0d ||
            worldSurfaceTiles >= Dimensions.HeightTiles)
        {
            return false;
        }

        if (WorldSurfaceTiles is double existing)
            return existing == worldSurfaceTiles;

        WorldSurfaceTiles = worldSurfaceTiles;
        return true;
    }

    private int GetIndex(int x, int y)
    {
        if ((uint)x >= (uint)Dimensions.WidthTiles)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= (uint)Dimensions.HeightTiles)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return GetUncheckedIndex(x, y);
    }
}
