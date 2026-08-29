namespace TerraRuntime.World;

/// <summary>
/// Contiguous column-major tile storage: index = x * height + y.
/// This matches vanilla .wld traversal order, keeping load/save scans sequential without dictating packet-section layout.
/// Mutable access is intended for the authoritative game thread.
/// </summary>
public sealed class WorldTileStore
{
    private readonly WorldTile[] _tiles;

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
        LiquidUpdates = new WorldLiquidUpdateQueue(dimensions);
        DirtySections = new DirtySectionTracker(dimensions);
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
    /// Network sections dirtied through the authoritative tile mutation API. Canonical world and snapshot
    /// loaders write the private backing span directly, so publishing an initial world does not dirty every section.
    /// </summary>
    public DirtySectionTracker DirtySections { get; }

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
        _tiles[index] = tile;
        DirtySections.MarkTileDirty(x, y);
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
