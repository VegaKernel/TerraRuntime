using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Final Cleanup / TileFrame / CheckDoorClosed for generated Obsidian doors only.</summary>
internal static class GenerationObsidianDoorFraming1458
{
    public static bool Check(WorldTileStore store, int x, int y)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        // The source TileFrame entry uses this five-tile exclusion.
        if (x <= 5 || y <= 5 || x >= width - 5 || y >= height - 5) return false;
        WorldTile touched = At(store, x, y);
        if (!touched.IsActive || touched.Type != 10 || !IsObsidianFrame(touched)) return false;
        int top = y - touched.FrameY % 54 / 18;
        if (top < 1 || top + 3 >= height) return false;

        bool complete = HellFortGenerator1458.Solid(At(store, x, top - 1), noDoors: false) &&
            HellFortGenerator1458.Solid(At(store, x, top + 3), noDoors: false);
        for (int row = 0; row < 3; row++)
        {
            WorldTile cell = At(store, x, top + row);
            complete &= cell.IsActive && cell.Type == 10;
        }
        if (complete) return false;
        // Do not import locked/foreign door semantics through a damaged footprint.
        for (int row = 0; row < 3; row++)
        {
            WorldTile cell = At(store, x, top + row);
            if (cell.IsActive && cell.Type == 10 && !IsObsidianFrame(cell))
                throw new InvalidOperationException("Damaged Obsidian door contains unsupported door semantics.");
        }
        bool changed = false;
        for (int row = 0; row < 3; row++)
        {
            ref WorldTile cell = ref At(store, x, top + row);
            if (!cell.IsActive || cell.Type != 10) continue;
            // KillTile under destroyObject/generation: preserve replacement bricks,
            // walls/liquid/wires; Item.NewItem returns before creating a door drop.
            cell.Type = 0;
            cell.FrameX = cell.FrameY = -1;
            cell.Shape = cell.TileColor = 0;
            cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive |
                WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            for (int tx = x - 1; tx <= x + 1; tx++)
            for (int ty = top + row - 1; ty <= top + row + 1; ty++)
            {
                ref WorldTile neighbour = ref At(store, tx, ty);
                if (neighbour.IsActive) continue;
                neighbour.Shape = neighbour.TileColor = 0;
                neighbour.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            }
            changed = true;
        }
        return changed;
    }

    private static bool IsObsidianFrame(in WorldTile tile) =>
        tile.FrameX is >= 0 and < 54 && tile.FrameY is >= 1026 and < 1080;

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
