using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // Bounded final WorldGen.FinishRemixWorld CheckLilyPad scan, 1.4.5.8.
    // Up to four separated water-kind pads over source-backed grass styles
    // 477/109/60/116 are independently admitted here. Neighbour interactions
    // and invalid-pad destruction need a separate full-helper differential.
    internal static void FinishRemixLilyPadScan(WorldTileStore store,
        GenerationTileFraming1458 framing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(framing);
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (width < 1000 || height < 100)
            throw new ArgumentOutOfRangeException(nameof(store));

        Span<int> padColumns = stackalloc int[4];
        int count = 0;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldTile tile = store.Get(x, y);
            if (tile.Type != 518)
                continue;
            if (count == padColumns.Length || !tile.IsActive || tile.LiquidKind != WorldLiquidKind.Water ||
                x < 6 || x >= width - 6 || y < 6 || y >= height - 51 ||
                !HasKnownGrassSupport(x, y))
                throw new NotSupportedException("Remix lily-pad scan needs a broader source fixture.");
            for (int prior = 0; prior < count; prior++)
                if (Math.Abs(x - padColumns[prior]) < 6)
                    throw new NotSupportedException("Nearby Remix lily pads need an interaction fixture.");
            padColumns[count++] = x;
        }
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (store.Get(x, y).Type == 518)
                framing.CheckLilyPadForRemix(x, y);
        }

        bool HasKnownGrassSupport(int x, int fromY)
        {
            for (int y = fromY + 1; y < height - 50; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive)
                    return tile.Type is 477 or 109 or 60 or 116;
            }
            return false;
        }
    }
}
