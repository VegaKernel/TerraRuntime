namespace TerraRuntime.World;

/// <summary>
/// Wakes the bounded vanilla-liquid work queue after an authoritative geometry mutation. Terraria immediately
/// rechecks liquid around changed tiles; without this wakeup a removed support block can leave water visually
/// suspended until the background discovery cursor eventually reaches the cell again.
/// </summary>
public static class VanillaWorldLiquidWakeup1458
{
    public static void WakeCellAndNeighbours(WorldTileStore tiles, int x, int y) =>
        WakeRegion(tiles, x, y, width: 1, height: 1);

    public static void WakeRegion(WorldTileStore tiles, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        int minX = Math.Max(0, x - 1);
        int minY = Math.Max(0, y - 1);
        int maxX = Math.Min(tiles.Dimensions.WidthTiles - 1, checked(x + width));
        int maxY = Math.Min(tiles.Dimensions.HeightTiles - 1, checked(y + height));

        for (int tx = minX; tx <= maxX; tx++)
        {
            for (int ty = minY; ty <= maxY; ty++)
            {
                if (tiles.LiquidUpdates.ActiveCount + tiles.LiquidUpdates.BufferedCount >=
                    VanillaWorldLiquidSimulator1458.MaximumPendingCells)
                {
                    return;
                }

                // TerrariaServer 1.4.5.8 Liquid.AddWater returns immediately for a zero-liquid cell.
                // Geometry wakeups therefore must not fill the active queue with empty neighbours ahead of the
                // actual liquid source; doing so can starve the source under a bounded per-tick change buffer.
                if (tiles.Get(tx, ty).LiquidAmount != 0)
                    _ = tiles.LiquidUpdates.TryEnqueue(tx, ty);
            }
        }
    }
}
