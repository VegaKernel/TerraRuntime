using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The Remix starting ridge, on the existing Underworld store and physical random stream.</summary>
internal static class UnderworldStartingMound1458
{
    internal static void Generate(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        var store = workspace.TileStore;
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        int left = (int)(width * .38f), right = (int)(width * .62f);
        int floor = height - 1, plateau = height - 135, high = height - 160;
        bool reachedPlateau = false;
        int waterLine = (workspace.VanillaLiquidLines ?? throw new InvalidOperationException("The Remix ridge requires retained liquid lines.")).WaterLine;
        UnderworldLiquidPreparation1458.Settle(store, waterLine, cancellationToken, workspace, new GenerationTileFraming1458(store, random, workspace, useWorldGenerationSolidity: true));
        for (int x = left; floor < height - 1 || x < right; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reachedPlateau)
            {
                floor -= random.Next(1, 4);
                reachedPlateau = floor < plateau;
            }
            else if (x >= right) floor = Math.Min(height - 1, floor + random.Next(1, 4));
            else
            {
                if ((x <= width / 2 - 5 || x >= width / 2 + 5) && random.Next(4) == 0)
                {
                    if (random.Next(3) == 0) floor += random.Next(-1, 2);
                    else if (random.Next(6) == 0) floor += random.Next(-2, 3);
                    else if (random.Next(8) == 0) floor += random.Next(-4, 5);
                }
                floor = Math.Clamp(floor, high, plateau);
            }
            for (int y = floor; y > floor - 20; y--) At(store, x, y).LiquidAmount = 0;
            for (int y = floor; y < height; y++) At(store, x, y) = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        }
        UnderworldLiquidPreparation1458.Settle(store, waterLine, cancellationToken, workspace, new GenerationTileFraming1458(store, random, workspace, useWorldGenerationSolidity: true));
        for (int x = left; x < right + 15; x++)
        for (int y = height - 300; y < plateau + 20; y++)
        {
            ref var tile = ref At(store, x, y);
            tile.LiquidAmount = 0;
            if (!tile.IsActive || tile.Type != 57) continue;
            bool exposed = false;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if ((dx != 0 || dy != 0) && !At(store, x + dx, y + dy).IsActive) exposed = true;
            if (exposed) tile.Type = AshTreeGrower1458.AshGrass;
        }
        for (int x = left; x < right + 15; x++)
        for (int y = height - 200; y < plateau + 20; y++)
            if (At(store, x, y) is { IsActive: true, Type: AshTreeGrower1458.AshGrass } &&
                !At(store, x, y - 1).IsActive && random.Next(3) == 0)
                AshTreeGrower1458.TryGrow(store, x, y, random);
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
