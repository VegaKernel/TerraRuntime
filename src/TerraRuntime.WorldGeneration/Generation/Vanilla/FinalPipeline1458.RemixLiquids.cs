using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // TerrariaServer 1.4.5.8 WorldGen.FinishRemixWorld, between its second scan
    // and AddTrees: QuickWater, spawn-area scrub/re-settle, then the final
    // QuickWater after decoration. The last call is deliberately a separate
    // operation because AddTrees and the palette are not owned here yet.
    internal static void FinishRemixFirstLiquidAndSpawnScrub(
        WorldTileStore store, int spawnX, int spawnY, bool randomSpawn,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (width < 1000 || height < 400 || spawnX < 0 || spawnX >= width ||
            spawnY < 14 || spawnY + 10 >= height)
            throw new ArgumentOutOfRangeException(nameof(store),
                "Remix liquid continuation requires source-sized terrain and a valid spawn probe.");

        var liquids = new VanillaWorldLiquidSimulator1458(store);
        liquids.QuickWaterDuringWorldGeneration(cancellationToken);
        if (randomSpawn)
            return;

        // Source repeats until the spawn probe stays dry. Cap a malformed
        // candidate rather than allowing a generation thread to loop forever.
        for (int attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = (int)(width * 0.38); x < (int)(width * 0.62) + 15; x++)
            for (int y = height - 200; y < height - 125; y++)
            {
                ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
                tile.LiquidAmount = 0;
                if (tile.Type == 58)
                    tile.Type = 57;
                if (tile.Type is 230 or 56 or 659)
                    tile.Flags &= ~WorldTileFlags.Active;
            }
            liquids.QuickWaterDuringWorldGeneration(cancellationToken);
            bool wet = false;
            for (int dy = -14; dy <= 10; dy++)
                wet |= store.Get(spawnX, spawnY + dy).LiquidAmount != 0;
            if (!wet)
                return;
        }
        throw new InvalidOperationException("Remix spawn-area liquid did not settle within 32 source-equivalent retries.");
    }

    internal static void FinishRemixFinalQuickWater(WorldTileStore store,
        CancellationToken cancellationToken)
        => new VanillaWorldLiquidSimulator1458(store).QuickWaterDuringWorldGeneration(cancellationToken);
}
