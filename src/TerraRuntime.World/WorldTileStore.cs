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
    {
        ArgumentNullException.ThrowIfNull(dimensions);

        long tileCount = (long)dimensions.WidthTiles * dimensions.HeightTiles;
        if (tileCount > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "World contains too many tiles for contiguous storage.");
        }

        Dimensions = dimensions;
        _tiles = new WorldTile[(int)tileCount];
        LiquidUpdates = new WorldLiquidUpdateQueue(dimensions);
    }

    public WorldDimensions Dimensions { get; }

    public WorldLiquidUpdateQueue LiquidUpdates { get; }

    /// <summary>
    /// Validated immutable world-surface geometry from the canonical runtime metadata. It is attached only
    /// after the complete .wld has passed transactional validation, so ad-hoc/test stores may leave it unset.
    /// </summary>
    public double? WorldSurfaceTiles { get; private set; }

    public int Count => _tiles.Length;

    public WorldTile Get(int x, int y) => _tiles[GetIndex(x, y)];

    public void Set(int x, int y, in WorldTile tile) => _tiles[GetIndex(x, y)] = tile;

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
