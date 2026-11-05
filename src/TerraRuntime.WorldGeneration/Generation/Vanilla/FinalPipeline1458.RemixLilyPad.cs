using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // Bounded final WorldGen.FinishRemixWorld CheckLilyPad scan, 1.4.5.8.
    // One water-kind pad over golf grass 477 is independently admitted here;
    // broader substrates, neighbours and invalid-pad destruction need their
    // own full-helper differential before this scan can be generalized.
    internal static void FinishRemixLilyPadScan(WorldTileStore store,
        GenerationTileFraming1458 framing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(framing);
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (width < 1000 || height < 100)
            throw new ArgumentOutOfRangeException(nameof(store));

        int count = 0;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldTile tile = store.Get(x, y);
            if (tile.Type != 518)
                continue;
            if (++count > 1 || !tile.IsActive || tile.LiquidKind != WorldLiquidKind.Water ||
                x < 6 || x >= width - 6 || y < 6 || y >= height - 51 ||
                !HasGolfGrassSupport(x, y))
                throw new NotSupportedException("Remix lily-pad scan needs a broader source fixture.");
        }
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (store.Get(x, y).Type == 518)
                framing.CheckLilyPadForRemix(x, y);
        }

        bool HasGolfGrassSupport(int x, int fromY)
        {
            for (int y = fromY + 1; y < height - 50; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive)
                    return tile.Type == 477;
            }
            return false;
        }
    }
}
