using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Bounded, source-reachable WorldGen.OverGrownDungeon owner (TerrariaServer 1.4.5.8).
/// Only a single exposed dungeon-brick hit is admitted, including independent
/// successful-tree, failed-tree/plant, and downward-vine continuations. Broader
/// dungeon maps remain outside this helper's preflight.
/// </summary>
internal static class GenerationOverGrownDungeon1458
{
    internal static void ApplySingleEnclosedHit(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        int dungeonSide,
        double surface,
        double rockLayer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        if (width < 110 || height < 110 || dungeonSide is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(workspace));
        if (surface < 0 || surface >= height || rockLayer < surface || rockLayer >= height)
            throw new ArgumentOutOfRangeException(nameof(surface));
        if (workspace.VanillaSeedProfile.Secret != VanillaSecretWorldSeed1458.None)
            throw new NotSupportedException("The bounded dungeon owner excludes dual-dungeon special-seed rejection.");

        WorldTileStore tiles = workspace.TileStore;
        int brickX = -1, brickY = -1;
        for (int x = 0; x < width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < height; y++)
            {
                WorldTile tile = At(x, y);
                if (tile.Type is not (0 or 1 or 41 or 60) || tile.Wall is not (0 or 7))
                    throw new NotSupportedException($"Unsupported dungeon input at ({x},{y}).");
                if (tile.Wall == 7 || tile.Type == 41)
                {
                    if (!tile.IsActive || tile.Type != 41 || tile.Wall != 7 || brickX >= 0)
                        throw new NotSupportedException("The bounded dungeon owner requires one active brick on one dungeon wall.");
                    brickX = x;
                    brickY = y;
                }
            }
        }
        if (brickX < 16 || brickX >= width - 16 || brickY < 16 || brickY >= height - 16 ||
            At(brickX - 1, brickY - 1).IsActive)
            throw new NotSupportedException("The dungeon hit must expose one upper diagonal.");
        bool plantContinuation = !At(brickX, brickY - 1).IsActive;
        bool treeContinuation = plantContinuation &&
            At(brickX - 1, brickY).IsActive && At(brickX - 1, brickY).Type == 60 &&
            At(brickX + 1, brickY).IsActive && At(brickX + 1, brickY).Type == 60;
        if (plantContinuation &&
            (At(brickX, brickY - 1).Type != 0 || At(brickX, brickY - 1).Wall != 0 ||
             At(brickX, brickY - 1).LiquidAmount != 0 ||
             !At(brickX - 1, brickY).IsActive ||
             !At(brickX + 1, brickY).IsActive ||
             !treeContinuation && (At(brickX - 1, brickY).Type != 1 ||
                                   At(brickX + 1, brickY).Type != 1)))
            throw new NotSupportedException("Tree continuation requires paired stone or jungle-grass neighbours.");
        if (treeContinuation)
            for (int x = brickX - 2; x <= brickX + 2; x++)
                for (int y = brickY - 30; y < brickY; y++)
                    if (At(x, y).IsActive || At(x, y).Wall != 0 || At(x, y).LiquidAmount != 0)
                        throw new NotSupportedException("The growing dungeon tree requires a clear source canopy.");
        bool vineContinuation = !At(brickX, brickY + 1).IsActive;
        if (treeContinuation && vineContinuation)
            throw new NotSupportedException("Combined tree and vine continuation needs a separate source fixture.");
        if (vineContinuation)
            for (int y = brickY + 1; y <= brickY + 6; y++)
                if (At(brickX, y).IsActive || At(brickX, y).Type != 0 ||
                    At(brickX, y).Wall != 0 || At(brickX, y).LiquidAmount != 0)
                    throw new NotSupportedException("Vine continuation requires six empty cells below the brick.");

        var framing = new GenerationTileFraming1458(tiles, random, workspace, useWorldGenerationSolidity: false);

        // Source still draws both sample coordinates for every column, even after
        // the one dungeon wall has been encountered and converted.
        for (int i = 0; i < width; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(50, width / 2);
            if (dungeonSide >= 1)
                x = random.Next(width / 2, width - 50);
            int y = random.Next(50, height - 50);
            if (x != brickX || y != brickY)
                continue;

            int radius = random.Next(5, 16);
            for (int xx = x - radius; xx <= x + radius; xx++)
                for (int yy = y - radius; yy <= y + radius; yy++)
                {
                    ref WorldTile tile = ref At(xx, yy);
                    if (!tile.IsActive || tile.Type != 41)
                        continue;
                    // The admitted upper diagonal is empty, so the source's
                    // fully-enclosed rejection cannot fire. Its upper and lower
                    // centre neighbours block GrowTree/PlaceTile continuation.
                    tile.Type = 60;
                    tile.TileColor = 0;
                    if (plantContinuation &&
                        (!treeContinuation ||
                         !TreeGrower1458.TryGrow(tiles, xx, yy, random, framing: framing)))
                        PlacePlant(xx, yy - 1);
                    if (vineContinuation && random.Next(3) != 0)
                    {
                        PlaceVine(xx, yy + 1);
                        PlaceVine(xx, yy + 2);
                        PlaceVine(xx, yy + 3);
                        if (random.Next(4) != 0)
                            PlaceVine(xx, yy + 4);
                        if (random.Next(3) != 0)
                            PlaceVine(xx, yy + 5);
                        if (random.Next(2) != 0)
                            PlaceVine(xx, yy + 6);
                    }
                }
        }

        ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];

        void PlaceVine(int x, int y)
        {
            ref WorldTile tile = ref At(x, y);
            tile.Type = 62;
            tile.Flags |= WorldTileFlags.Active;
            framing.SquareTileFrame(x, y);
        }

        void PlacePlant(int x, int y)
        {
            // GrowTree refuses the admitted stone neighbours before drawing RNG.
            // PlaceTile(61) then follows its ordinary jungle-grass support path.
            ref WorldTile tile = ref At(x, y);
            tile.Flags |= WorldTileFlags.Active;
            if (random.Next(16) == 0 && y > surface)
            {
                tile.Type = 69;
            }
            else if (random.Next(60) == 0 && y > rockLayer)
            {
                tile.Type = 61;
                tile.FrameX = 144;
            }
            else if (random.Next(230) == 0 && y > rockLayer)
            {
                tile.Type = 61;
                tile.FrameX = 162;
            }
            else if (random.Next(15) == 0)
            {
                tile.Type = 61;
                tile.FrameX = (short)(random.Next(3) != 0
                    ? random.Next(2) * 18 + 108
                    : random.Next(13) * 18 + 180);
            }
            else
            {
                tile.Type = 61;
                tile.FrameX = (short)(random.Next(6) * 18);
            }
            framing.SquareTileFrame(x, y);
        }
    }
}
