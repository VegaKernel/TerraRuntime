using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // WorldGen.FinishTenthAnniversaryWorld, TerrariaServer 1.4.5.8. The ForTheWorthy
    // combination reaches exactly this paint phase and skips sky conversion, crystal
    // conversion and chest prefix improvement. Other combinations are admitted only
    // when their source-only side effects are known to be absent in the supplied world.
    internal static void FinishTenthAnniversaryWorld(Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        VanillaWorldSeedProfile1458 profile = workspace.VanillaSeedProfile;
        if (!profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10))
            throw new InvalidOperationException("Anniversary finalization requires its source seed profile.");
        bool worthy = profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy);
        bool drunk = profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld);
        bool remix = profile.Has(VanillaSpecialWorldSeed1458.Remix);
        bool bees = profile.Has(VanillaSpecialWorldSeed1458.NotTheBees);
        bool constant = profile.Has(VanillaSpecialWorldSeed1458.TheConstant);
        bool paints = !remix && !bees && !constant;
        if (paints && !worthy && !drunk)
            throw new NotSupportedException("Anniversary sky-island conversion requires its source tree and tile owners.");
        if (!worthy)
            AnniversaryChestPrefix1458.ValidateQualifiedChests(workspace);
        if (!workspace.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Anniversary painting requires retained world surface.");

        var grid = new RuntimeGrid(workspace);
        if (paints)
        {
            bool onlyShimmerOceans = drunk && !remix && !profile.Has(VanillaSpecialWorldSeed1458.Zenith) && !bees;
            PaintAnniversaryDungeon(grid, cancellationToken);
            if (!onlyShimmerOceans && !profile.Has(VanillaSecretWorldSeed1458.MolePeople))
                PaintAnniversaryLivingTrees(grid, cancellationToken);
            PaintAnniversaryTemple(grid, bees, cancellationToken);
            if (!onlyShimmerOceans)
                PaintAnniversaryClouds(grid, cancellationToken);
            PaintAnniversarySand(grid, layers.WorldSurface, cancellationToken);
            if (!onlyShimmerOceans)
                PaintAnniversaryPyramids(grid, cancellationToken);
        }

        if (!worthy)
            for (int x = 50; x < grid.Width - 50; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int y = 50; y < grid.Height - 50; y++)
                {
                    ref WorldTile tile = ref grid.At(x, y);
                    if (random.Next(4) != 0 || !tile.IsActive || tile.Type != 138 ||
                        tile.FrameX != 0 || tile.FrameY != 0)
                        continue;
                    grid.At(x, y).Type = 665;
                    grid.At(x, y + 1).Type = 665;
                    grid.At(x + 1, y).Type = 665;
                    grid.At(x + 1, y + 1).Type = 665;
                }
            }
        if (!worthy)
            AnniversaryChestPrefix1458.ImproveQualifiedChests(workspace, random, cancellationToken);
    }

    private static void PaintAnniversaryDungeon(RuntimeGrid grid, CancellationToken cancellationToken)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < grid.Height; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && (DungeonGenerationTiles1458.IsDungeonTile(tile.Type) ||
                    tile.Type is 481 or 482 or 483 ||
                    tile.Type == 19 && tile.FrameY / 18 is >= 6 and <= 12))
                    tile.TileColor = 24;
                if (DungeonGenerationTiles1458.IsDungeonWall(tile.Wall))
                    tile.WallColor = 24;
            }
        }
    }

    private static void PaintAnniversaryLivingTrees(RuntimeGrid grid, CancellationToken cancellationToken)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < grid.Height; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive)
                {
                    bool paint = tile.Wall == 244 || tile.Type is 192 or 191;
                    if (!paint && tile.Type is 52 or 382)
                    {
                        int top = y;
                        while (top > 20 && grid.At(x, top).IsActive &&
                            IsVine(grid.At(x, top).Type)) top--;
                        paint = grid.At(x, top).Type == 192;
                    }
                    if (!paint && tile.Type == 187)
                    {
                        int below = y;
                        while (below < grid.Height - 1 && grid.At(x, below).Type == 187) below++;
                        paint = grid.At(x, below).Type == 192;
                    }
                    if (paint) tile.TileColor = 12;
                }
                if (tile.Wall == 244) tile.WallColor = 12;
            }
        }
    }

    private static bool IsVine(ushort type) => type is 52 or 382 or 62 or 115 or 205 or 528 or 636 or 638;

    private static void PaintAnniversaryTemple(RuntimeGrid grid, bool bees, CancellationToken cancellationToken)
    {
        for (int x = 10; x < grid.Width - 10; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 10; y < grid.Height - 10; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && (tile.Type == 226 ||
                    tile.Type == 61 && grid.At(x, y + 1).Type == 226 ||
                    tile.Type == 137 && tile.FrameY / 18 is >= 1 and <= 4))
                    tile.TileColor = bees ? (byte)0 : (byte)10;
                if (tile.Wall == 87) tile.WallColor = 5;
            }
        }
    }

    private static void PaintAnniversaryClouds(RuntimeGrid grid, CancellationToken cancellationToken)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < grid.Height; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && tile.Type is 189 or 196 or 460 or 717 or 718 or 719)
                    tile.TileColor = 12;
                if (tile.Wall == 73) tile.WallColor = 12;
            }
        }
    }

    private static void PaintAnniversarySand(RuntimeGrid grid, double worldSurface,
        CancellationToken cancellationToken)
    {
        for (int x = 20; x < grid.Width - 20; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 20; y < grid.Height - 20; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && tile.Type is 53 or 396 or 397)
                {
                    tile.TileColor = 7;
                    if (y > worldSurface)
                    {
                        for (int offset = 1; offset <= 2; offset++)
                        {
                            if (grid.At(x, y - offset).Type is 165 or 185 or 186 or 187)
                                grid.At(x, y - offset).TileColor = 7;
                            if (grid.At(x, y + offset).Type == 165)
                                grid.At(x, y + offset).TileColor = 7;
                        }
                    }
                }
                if (tile.Wall is 187 or 216) tile.WallColor = 7;
            }
        }
    }

    private static void PaintAnniversaryPyramids(RuntimeGrid grid, CancellationToken cancellationToken)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < grid.Height; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && tile.Type == 151) tile.TileColor = 12;
                if (tile.Wall == 34) tile.WallColor = 12;
            }
        }
    }
}
