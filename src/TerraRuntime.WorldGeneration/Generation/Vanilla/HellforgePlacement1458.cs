using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary WorldGen Hellforges pass and its Place3x2(77) subset, TerrariaServer 1.4.5.8.</summary>
internal static class HellforgePlacement1458
{
    internal const ushort Hellforge = 77;

    public static int Generate(WorldTileStore store, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken, Workspace? workspace = null)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles, placed = 0;
        for (int forge = 0; forge < width / 200; forge++)
        {
            int placementFailures = 0;
            for (int sample = 0; ; sample++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Vanilla doesn't count samples outside house walls. Bound that otherwise infinite search without
                // placing on an arbitrary ash floor or accepting an incomplete candidate as a replacement.
                if (sample == 1_000_000)
                    throw new InvalidOperationException("Hellforge wall search exhausted its safety budget.");
                int x = random.Next(1, width), y = random.Next(height - 250, height - 30);
                if (At(store, x, y).Wall is not (13 or 14)) continue;
                while (!At(store, x, y).IsActive && y < height - 20) y++;
                y--;
                TryPlaceTile(store, random, x, y, workspace);
                if (At(store, x, y).Type == Hellforge) { placed++; break; }
                if (++placementFailures >= 10_000) break;
            }
        }
        return placed;
    }

    internal static bool TryPlaceTile(WorldTileStore store, IWorldGenerationVanillaRandom random, int centerX, int bottomY, Workspace? workspace = null) =>
        GenerationDecorationPlacement1458.TryPlaceTile3x2(store, random, centerX, bottomY, Hellforge, 0, workspace: workspace);

    internal static bool TryPlace(WorldTileStore store, int centerX, int bottomY, Workspace? workspace = null) =>
        GenerationDecorationPlacement1458.TryPlace3x2(store, centerX, bottomY, Hellforge, 0, workspace: workspace);

    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
