using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Shared source-backed floor furniture placement on unpublished generation workspaces.</summary>
internal static class GenerationFurniturePlacement1458
{
    internal static bool Place(Workspace workspace, int x, int bottom, ushort type, int style, bool faceRight = false,
        bool crackedBricksSolid = true)
    {
        (int offset, int width, int height, int frameX, int frameY) = type switch
        {
            14 => (-1, 3, 2, style * 54, 0),
            15 => (0, 1, 2, 0, style * 40),
            18 => (0, 2, 1, style * 36, 0),
            33 => (0, 1, 1, 0, style * 22),
            79 => (-1, 4, 2, faceRight ? 72 : 0, style * 36),
            87 => (-1, 3, 2, style * 54, 0),
            88 => (-1, 3, 2, style * 54, 0),
            89 => (-1, 3, 2, style * 54, 0),
            90 => (-1, 4, 2, faceRight ? 72 : 0, style * 36),
            93 => (0, 1, 3, 0, style * 54),
            100 => (-1, 2, 2, 0, style * 36),
            101 => (-1, 3, 4, style * 54, 0),
            104 => (0, 2, 5, style * 36, 0),
            105 => (0, 2, 3, style * 36, 0),
            103 => (0, 2, 1, 0, 0),
            354 or 355 => (-1, 3, 3, 0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        WorldTileStore store = workspace.TileStore;
        if (x < 5 || x > store.Dimensions.WidthTiles - 5 || bottom < 6 || bottom > store.Dimensions.HeightTiles - 5) return false;
        // Beds/sofas call Place4x2 directly; all other choices go through PlaceTile's inactive-anchor cleanup.
        if (type is not (79 or 90) && !At(store, x, bottom).IsActive)
            HellFortGenerator1458.ClearPlacementAnchor(ref At(store, x, bottom));
        int left = x + offset, top = bottom - height + 1;
        for (int column = left; column < left + width; column++)
        {
            WorldTile support = At(store, column, bottom + 1);
            bool supported = type == 33
                ? support.IsActive && !support.IsActuated && GenerationFurnitureTiles1458.IsTable(support.Type)
                : type == 103 ? support.IsActive && GenerationFurnitureTiles1458.IsTable(support.Type)
                : type == 100 ? support.IsActive && !support.IsActuated &&
                    (support.Shape == 0 && IsSolid(support) || GenerationFurnitureTiles1458.IsTable(support.Type))
                : support.IsActive && !support.IsActuated && (support.Shape == 0 || support.Type == 19 && support.Shape is 2 or 3) && IsSolid(support);
            if (!supported) return false;
            for (int row = top; row <= bottom; row++)
                if (At(store, column, row).IsActive || (type == 93 && At(store, column, row).LiquidAmount > 0)) return false;
        }
        // Preserve the footprint if the shared chest registry refuses a dresser (capacity/duplicate).
        Span<WorldTile> dresserBefore = stackalloc WorldTile[6];
        if (type == 88)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 3; dx++) dresserBefore[dy * 3 + dx] = At(store, left + dx, top + dy);
        for (int dy = 0; dy < height; dy++)
        for (int dx = 0; dx < width; dx++)
        {
            ref WorldTile tile = ref At(store, left + dx, top + dy);
            tile.Flags |= WorldTileFlags.Active; tile.Type = type;
            tile.FrameX = (short)(frameX + dx * 18); tile.FrameY = (short)(frameY + dy * 18);
        }
        if (type == 88 && !workspace.TryAddGeneratedChest(left, top, string.Empty, []))
        {
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 3; dx++) At(store, left + dx, top + dy) = dresserBefore[dy * 3 + dx];
            return false;
        }
        return true;
        bool IsSolid(WorldTile tile) => (crackedBricksSolid || tile.Type is not (481 or 482 or 483)) && VanillaTileCollisionCatalog.IsSolid(tile.TileType);
    }
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
