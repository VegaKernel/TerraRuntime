namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Protocol-neutral immutable tile-space dimensions used by runtime invariants that need only world bounds.
/// Storage/layout implementations may project their richer dimension type into this contract without creating
/// a Core -> World dependency.
/// </summary>
public readonly record struct WorldTileBounds
{
    public WorldTileBounds(int widthTiles, int heightTiles)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(widthTiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(heightTiles, 1);
        WidthTiles = widthTiles;
        HeightTiles = heightTiles;
    }

    public int WidthTiles { get; }

    public int HeightTiles { get; }
}
