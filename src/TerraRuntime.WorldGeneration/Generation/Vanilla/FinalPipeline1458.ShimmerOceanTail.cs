using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // Main.onlyShimmerOceanWorlds / SpecialSeedFeatures, TerrariaServer 1.4.5.8.
    internal static bool HasOnlyShimmerOceanTail(VanillaWorldSeedProfile1458 profile) =>
        profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld) &&
        profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10) &&
        !profile.Has(VanillaSpecialWorldSeed1458.Remix) &&
        !profile.Has(VanillaSpecialWorldSeed1458.Zenith) &&
        !profile.Has(VanillaSpecialWorldSeed1458.NotTheBees);

    // Called after the individual special-seed finishers and before SecretSeed.FinalizeSecretSeeds.
    // The two official helpers consume no generation RNG and retain all unrelated tile fields.
    internal static void ApplyOnlyShimmerOceanTail(Workspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!HasOnlyShimmerOceanTail(workspace.VanillaSeedProfile))
            return;
        if (!workspace.TryGetSpawn(out WorldGenerationPoint spawn))
            throw new InvalidOperationException("Shimmer-ocean cleanup requires the generated spawn point.");

        WorldTileStore tiles = workspace.TileStore;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        int middle = width / 2;
        bool shimmerLeft = spawn.X <= middle;
        int underworld = height - 200; // Main.UnderworldLayer, TerrariaServer 1.4.5.8.
        for (int x = shimmerLeft ? 0 : middle + 1; x < (shimmerLeft ? middle : width); x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < underworld; y++)
            {
                ref WorldTile tile = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
                if (DungeonGenerationTiles1458.IsDungeonWall(tile.Wall))
                    continue;
                if (tile.LiquidAmount > 0 && tile.LiquidKind is not (WorldLiquidKind.Shimmer or WorldLiquidKind.Honey))
                    tile.LiquidKind = WorldLiquidKind.Shimmer;
                if (tile.Type is 373 or 374 or 375)
                    tile.Type = 709;
            }
        }

        int surface = workspace.TryGetLayers(out WorldGenerationLayers layers)
            ? (int)layers.WorldSurface
            : throw new InvalidOperationException("Rainbow sand cleanup requires generated terrain layers.");
        for (int x = 20; x < width - 20; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 20; y < height - 20; y++)
            {
                byte color = RainbowPaintAt(x, y);
                ref WorldTile tile = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
                if (tile.IsActive && tile.Type is 53 or 396 or 397)
                {
                    tile.TileColor = color;
                    if (y > surface || tile.Wall is 187 or 216)
                    {
                        if (tiles.Tiles[tiles.GetUncheckedIndex(x, y - 1)].Type is 165 or 185 or 186 or 187)
                            tiles.Tiles[tiles.GetUncheckedIndex(x, y - 1)].TileColor = color;
                        if (tiles.Tiles[tiles.GetUncheckedIndex(x, y - 2)].Type is 165 or 185 or 186 or 187)
                            tiles.Tiles[tiles.GetUncheckedIndex(x, y - 2)].TileColor = color;
                        if (tiles.Tiles[tiles.GetUncheckedIndex(x, y + 1)].Type == 165)
                            tiles.Tiles[tiles.GetUncheckedIndex(x, y + 1)].TileColor = color;
                        if (tiles.Tiles[tiles.GetUncheckedIndex(x, y + 2)].Type == 165)
                            tiles.Tiles[tiles.GetUncheckedIndex(x, y + 2)].TileColor = color;
                    }
                }
                if (tile.Wall is 187 or 216)
                    tile.WallColor = 25;
            }
        }
    }

    // WorldGen.GetRainbowPaintIDForPosition(..., wiggly:true), TerrariaServer 1.4.5.8.
    internal static byte RainbowPaintAt(int x, int y)
    {
        int shiftedX = x + (int)(Math.Sin((float)(y % 50) / 50f * (MathF.PI * 2f)) * 10.0) - 5;
        return (byte)(13 + shiftedX % 44 % 11);
    }
}
