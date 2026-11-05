using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // WorldGen.FinishTenthAnniversaryWorldPaintTreesAndMushrooms, TerrariaServer 1.4.5.8.
    // This follows SecretSeed.FinalizeSecretSeeds, not the earlier anniversary paint pass.
    internal static void PaintAnniversaryTreesAndMushrooms(
        Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        VanillaWorldSeedProfile1458 profile = workspace.VanillaSeedProfile;
        if (!profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10))
            throw new InvalidOperationException("Anniversary tree and mushroom paint requires its source seed profile.");
        if (!workspace.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Anniversary paint requires retained terrain layers.");

        WorldTileStore tiles = workspace.TileStore;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        bool remix = profile.Has(VanillaSpecialWorldSeed1458.Remix);
        if (width <= 40 || height <= 40 || remix && height < 500)
            throw new NotSupportedException("Anniversary paint requires source-sized generation bounds.");

        int treeStart = remix ? (int)layers.WorldSurface : 20;
        byte treeColor = (byte)random.Next(1, 13);
        bool previousColumnHadTree = false;
        for (int x = 20; x < width - 20; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool columnHasTree = false;
            for (int y = treeStart; y < height - 20; y++)
            {
                ref WorldTile tile = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
                if (!tile.IsActive || tile.Type is not (5 or 323 or 596 or 616))
                    continue;
                tile.TileColor = treeColor;
                columnHasTree = true;
                previousColumnHadTree = true;
            }
            if (previousColumnHadTree && !columnHasTree)
            {
                previousColumnHadTree = false;
                treeColor++;
                if (treeColor > 12)
                    treeColor = 1;
            }
        }

        int mushroomEnd = height - 20;
        byte mushroomColor = (byte)random.Next(1, 13);
        if (remix)
        {
            mushroomColor = 2;
            mushroomEnd = height - 500;
            int bandWidth = random.Next(5, 31);
            if (random.Next(2) == 0)
                bandWidth = random.Next(5, 16);
            for (int x = 20; x < width - 20; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (x % bandWidth == 0)
                {
                    mushroomColor++;
                    if (mushroomColor > 12)
                        mushroomColor = 1;
                }
                for (int y = height - 450; y < height - 20; y++)
                    PaintMushroomCell(tiles, x, y, mushroomColor);
            }
        }

        mushroomColor = (byte)random.Next(1, 13);
        int countdown = 0;
        for (int x = 20; x < width - 20; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 20; y < mushroomEnd; y++)
                if (PaintMushroomCell(tiles, x, y, mushroomColor))
                    countdown = 10;
            countdown--;
            if (countdown == 0)
            {
                mushroomColor += (byte)random.Next(1, 3);
                if (mushroomColor > 12)
                    mushroomColor = 1;
            }
        }
    }

    private static bool PaintMushroomCell(WorldTileStore tiles, int x, int y, byte color)
    {
        ref WorldTile tile = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
        bool mushroom = tile.IsActive &&
            (tile.Type is 70 or 578 or 190 or 71 or 528 || tile.Type == 519 && tile.FrameY == 90);
        bool wall = tile.Wall is 80 or 74;
        if (mushroom)
            tile.TileColor = color;
        if (wall)
            tile.WallColor = color;
        return mushroom || wall;
    }
}
