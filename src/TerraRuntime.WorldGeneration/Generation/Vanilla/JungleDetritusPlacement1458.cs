using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>WorldGen.PlaceJunglePlant's ordinary type-233 3x2 placement on a clear footprint.</summary>
internal static class JungleDetritusPlacement1458
{
    public static bool TryPlace(WorldTileStore store, int x, int y, int style)
    {
        // Existing vegetation sampling selects only styleY=0, styles 0..7. Do not fabricate a
        // one-cell substitute when the real footprint fails. Active replacement/cascades stay unadmitted.
        if ((uint)style >= 8 || x < 5 || y < 5 ||
            x > store.Dimensions.WidthTiles - 5 || y > store.Dimensions.HeightTiles - 5) return false;
        for (int dx = -1; dx <= 1; dx++)
        {
            WorldTile floor = store.Get(x + dx, y + 1);
            if (!floor.IsActive || floor.IsActuated || floor.Type != 60 || floor.Shape != 0) return false;
            for (int dy = -1; dy <= 0; dy++)
                if (store.Get(x + dx, y + dy).IsActive) return false;
        }
        WorldTile paint = store.Get(x, y + 1);
        const WorldTileFlags coating = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 0; dy++)
        {
            WorldTile cell = store.Get(x + dx, y + dy);
            cell.Type = 233;
            cell.FrameX = (short)(style * 54 + (dx + 1) * 18);
            cell.FrameY = (short)((dy + 1) * 18);
            cell.Flags = (cell.Flags & ~coating) | WorldTileFlags.Active | (paint.Flags & coating);
            cell.TileColor = paint.TileColor;
            // Direct PlaceJunglePlant preserves the independent wall/wires/liquid and copies only block paint.
            store.SetInitialPopulationTile(x + dx, y + dy, in cell);
        }
        return true;
    }
}
