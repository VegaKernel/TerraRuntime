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
    }

    public WorldDimensions Dimensions { get; }

    public int Count => _tiles.Length;

    public WorldTile Get(int x, int y) => _tiles[GetIndex(x, y)];

    public void Set(int x, int y, in WorldTile tile) => _tiles[GetIndex(x, y)] = tile;

    internal Span<WorldTile> Tiles => _tiles;

    internal int GetUncheckedIndex(int x, int y) => (x * Dimensions.HeightTiles) + y;

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
