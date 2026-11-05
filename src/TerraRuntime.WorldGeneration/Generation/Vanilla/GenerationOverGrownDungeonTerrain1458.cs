using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Dungeon-wall terrain slice of WorldGen.OverGrownDungeon (TerrariaServer 1.4.5.8).
/// Any number of wall samples and ordinary dungeon-brick conversions are admitted;
/// sites that reach GrowTree or PlaceTile are left to the separate owner.
/// </summary>
internal static class GenerationOverGrownDungeonTerrain1458
{
    internal static void Apply(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        int dungeonSide,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        if (width < 110 || height < 110 || dungeonSide is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(workspace));
        if (workspace.VanillaSeedProfile.Secret != VanillaSecretWorldSeed1458.None)
            throw new NotSupportedException("Dual-dungeon source rejection is not admitted here.");

        WorldTileStore tiles = workspace.TileStore;
        // The source's only physical continuation in this slice is brick -> jungle
        // grass. A brick touching unsupported tree/vine placement is refused before
        // the first world mutation or RNG draw, regardless of whether a random
        // radius would happen to reach it in this particular seed.
        for (int x = 0; x < width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < height; y++)
            {
                WorldTile tile = At(x, y);
                if (tile.Type is not (0 or 1 or 2 or 5 or 41 or 43 or 44 or 48 or 52 or 59 or 60 or 62 or
                                      147 or 161 or 189 or 225 or 230 or 232 or 373 or 375 or 382 or 384 or
                                      596 or 616) ||
                    tile.Wall is not (0 or 7 or 8 or 9 or 86))
                    throw new NotSupportedException($"Unsupported dungeon terrain at ({x},{y}).");
                if (!tile.IsActive || tile.Type is not (41 or 43 or 44))
                    continue;
                if (x < 16 || x >= width - 16 || y < 16 || y >= height - 16)
                    throw new NotSupportedException("Dungeon brick is too close to the source sampling border.");
                if (!IsGuarded(x, y) &&
                    (!At(x, y - 1).IsActive || !At(x, y + 1).IsActive))
                    throw new NotSupportedException("An exposed dungeon brick needs its GrowTree or PlaceTile continuation.");
            }
        }

        for (int i = 0; i < width; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(50, width / 2);
            if (dungeonSide >= 1)
                x = random.Next(width / 2, width - 50);
            int y = random.Next(50, height - 50);
            if (At(x, y).Wall is not (7 or 8 or 9))
                continue;
            int radius = random.Next(5, 16);
            for (int xx = x - radius; xx <= x + radius; xx++)
                for (int yy = y - radius; yy <= y + radius; yy++)
                {
                    ref WorldTile tile = ref At(xx, yy);
                    if (!tile.IsActive || tile.Type is not (41 or 43 or 44) || IsGuarded(xx, yy))
                        continue;
                    tile.Type = 60;
                    tile.TileColor = 0;
                }
        }

        ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];

        bool IsGuarded(int x, int y)
        {
            bool exposedAbove = !At(x - 1, y - 1).IsActive ||
                                !At(x, y - 1).IsActive ||
                                !At(x + 1, y - 1).IsActive;
            return !exposedAbove && At(x - 1, y).IsActive &&
                   At(x + 1, y).IsActive && At(x, y + 1).IsActive;
        }
    }
}
