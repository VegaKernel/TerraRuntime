using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Final Cleanup's incomplete desert-boulder/antlion-larva removal, not general object framing.</summary>
internal static class GenerationDesertObjectFraming1458
{
    public static bool Check(WorldTileStore store, int x, int y)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        if (x <= 5 || y <= 5 || x >= width - 5 || y >= height - 5) return false;
        WorldTile touched = At(store, x, y);
        if (!touched.IsActive || touched.Type is not (484 or 485)) return false;
        if (!SupportedFrame(in touched))
            throw new InvalidOperationException($"Unsupported generated desert object {touched.Type} frame {touched.FrameX},{touched.FrameY} at {x},{y}.");
        int styleX = touched.FrameX / 36 * 36;
        int left = x - touched.FrameX / 18 % 2, top = y - touched.FrameY / 18;
        if (left <= 5 || top <= 5 || left + 1 >= width - 5 || top + 1 >= height - 5) return false;
        bool complete = true;
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile cell = At(store, left + dx, top + dy);
            complete &= cell.IsActive && cell.Type == touched.Type && cell.FrameX == styleX + dx * 18 && cell.FrameY == dy * 18;
            if (cell.IsActive && cell.Type == touched.Type && !SupportedFrame(in cell))
                throw new InvalidOperationException("Damaged desert object contains unsupported frames.");
        }
        if (complete) return false;
        // WorldGen.Check2x2(484)/CheckSuper(485): a missing/replaced cell invalidates the object.
        // destroyObject removes only matching active cells. Loading/generation suppresses
        // the boulder projectile, larva NPC and Item.NewItem; foreign replacement tiles survive.
        // The larva support-only CheckSuper branch remains outside this footprint slice.
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile cell = ref At(store, left + dx, top + dy);
            if (!cell.IsActive || cell.Type != touched.Type) continue;
            cell.Type = 0;
            cell.FrameX = cell.FrameY = -1;
            cell.Shape = cell.TileColor = 0;
            cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive |
                WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            for (int tx = left + dx - 1; tx <= left + dx + 1; tx++)
            for (int ty = top + dy - 1; ty <= top + dy + 1; ty++)
            {
                ref WorldTile neighbour = ref At(store, tx, ty);
                if (neighbour.IsActive) continue;
                neighbour.Shape = neighbour.TileColor = 0;
                neighbour.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            }
        }
        return true;
    }

    private static bool SupportedFrame(in WorldTile tile) =>
        tile.FrameY is 0 or 18 && tile.FrameX >= 0 && tile.FrameX % 18 == 0 &&
        tile.FrameX <= (tile.Type == 484 ? 18 : 126);

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
