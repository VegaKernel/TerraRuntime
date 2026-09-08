using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary (not Drunk/Remix) Underworld ash-grass and Tree_Ash scans, immediately before AddHellHouses.</summary>
internal static class UnderworldVegetation1458
{
    public static void Generate(WorldTileStore store, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        GrowGrass(store, random, cancellationToken);
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        for (int x = 25; x < width - 25; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEdgeForest(x, width)) continue;
            for (int y = height - 200; y < height - 50; y++)
                if (At(store, x, y) is { IsActive: true, Type: AshTreeGrower1458.AshGrass } && !At(store, x, y - 1).IsActive && random.Next(3) == 0)
                    AshTreeGrower1458.TryGrow(store, x, y, random);
        }
    }

    internal static void GrowGrass(WorldTileStore store, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        if (height < 301) throw new ArgumentOutOfRangeException(nameof(store), "Underworld vegetation needs its complete source scan domain.");
        for (int x = 25; x < width - 25; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEdgeForest(x, width)) continue;
            // Next(-1,2) belongs to EACH loop-condition evaluation, including the terminating one.
            for (int y = height - 300; y < height - 100 + random.Next(-1, 2); y++)
            {
                ref WorldTile tile = ref At(store, x, y);
                if (!tile.IsActive || tile.Type != 57) continue;
                bool exposed = false;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if ((dx != 0 || dy != 0) && !At(store, x + dx, y + dy).IsActive) exposed = true;
                if (exposed) tile.Type = AshTreeGrower1458.AshGrass;
            }
        }
    }

    internal static bool IsEdgeForest(int x, int width) => x < width * 0.17 || x > width * 0.83;
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
