using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Isolated mutable tile workspace for a candidate generated world. Writes bypass live-world dirty tracking because
/// the store is not authoritative or network-visible until the caller explicitly accepts the completed candidate.
/// </summary>
public sealed class RuntimeWorldGenerationWorkspace : IWorldGenerationWorkspace
{
    private const WorldGenerationTileFlags KnownFlags =
        WorldGenerationTileFlags.Active |
        WorldGenerationTileFlags.WireRed |
        WorldGenerationTileFlags.WireBlue |
        WorldGenerationTileFlags.WireGreen |
        WorldGenerationTileFlags.WireYellow |
        WorldGenerationTileFlags.Actuator |
        WorldGenerationTileFlags.Inactive |
        WorldGenerationTileFlags.InvisibleBlock |
        WorldGenerationTileFlags.InvisibleWall |
        WorldGenerationTileFlags.FullbrightBlock |
        WorldGenerationTileFlags.FullbrightWall;

    public RuntimeWorldGenerationWorkspace(int widthTiles, int heightTiles)
    {
        Dimensions = new WorldDimensions(widthTiles, heightTiles);
        TileStore = new WorldTileStore(Dimensions);
    }

    public WorldDimensions Dimensions { get; }
    public WorldTileStore TileStore { get; }
    public int WidthTiles => Dimensions.WidthTiles;
    public int HeightTiles => Dimensions.HeightTiles;

    public bool TryGetTile(int x, int y, out WorldGenerationTile tile)
    {
        if (!Contains(x, y))
        {
            tile = default;
            return false;
        }

        WorldTile source = TileStore.Get(x, y);
        tile = new WorldGenerationTile(
            source.Type,
            source.Wall,
            source.FrameX,
            source.FrameY,
            (WorldGenerationTileFlags)source.Flags,
            source.LiquidAmount,
            source.TileColor,
            source.WallColor,
            source.Shape,
            (WorldGenerationLiquidKind)source.LiquidKind);
        return true;
    }

    public bool TrySetTile(int x, int y, in WorldGenerationTile tile)
    {
        if (!Contains(x, y) || !IsValid(tile))
            return false;

        var target = new WorldTile
        {
            Type = tile.Type,
            Wall = tile.Wall,
            FrameX = tile.FrameX,
            FrameY = tile.FrameY,
            Flags = (WorldTileFlags)tile.Flags,
            LiquidAmount = tile.LiquidAmount,
            TileColor = tile.TileColor,
            WallColor = tile.WallColor,
            Shape = tile.Shape,
            LiquidKind = (WorldLiquidKind)tile.LiquidKind
        };

        // Generation owns this unpublished store exclusively. Marking every generated section dirty would only
        // manufacture network invalidation work for a world that no connection can observe yet.
        TileStore.Tiles[TileStore.GetUncheckedIndex(x, y)] = target;
        return true;
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)WidthTiles && (uint)y < (uint)HeightTiles;

    private static bool IsValid(in WorldGenerationTile tile)
    {
        if ((tile.Flags & ~KnownFlags) != 0 || tile.Shape > 5 || !Enum.IsDefined(tile.LiquidKind))
            return false;

        // Official-client-compatible generation may only emit tile IDs known by Terraria 1.4.5.8. Wall content
        // gets the same catalog validation once a source-verified WallID.Count catalog is admitted to Contracts;
        // inventing that bound here would be worse than leaving the storage-width check explicit.
        if (tile.Type >= VanillaTileIds.Count)
            return false;

        return true;
    }
}
