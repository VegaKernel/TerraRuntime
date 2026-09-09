using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary Underworld terrain, before the existing vegetation and HellFort phases.
/// Source: TerrariaServer 1.4.5.8 Underworld pass and its small TileRunner calls; no secret-seed admission.</summary>
internal static class UnderworldTerrain1458
{
    internal const int Ash = 57, Hellstone = 58, LiquidCavity = -2;

    public static void Generate(WorldTileStore store, IWorldGenerationVanillaRandom random,
        double worldSurface, VanillaLiquidLines1458 lines, CancellationToken cancellationToken)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        if (width <= 40 || height < 300 || !double.IsFinite(worldSurface) || worldSurface < 0 ||
            worldSurface >= height - 200 || lines.WaterLine < 0 || lines.LavaLine < lines.WaterLine || lines.LavaLine >= height)
            throw new ArgumentOutOfRangeException(nameof(lines));

        CarveAndFill(store, random, cancellationToken);
        var runner = new SmallTerrainRunner1458(store, random, worldSurface, lines, cancellationToken);
        for (int x = 0; x < width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (random.Next(50) != 0) continue;
            int y = height - 65;
            while (!At(store, x, y).IsActive && y > height - 135) y--;
            // Vanilla scans this column but starts the runner at a separately sampled X.
            runner.Run(random.Next(0, width), y + random.Next(20, 50), random.Next(15, 20), 1000,
                Ash, true, 0, random.Next(1, 3), true);
        }

        UnderworldLiquidPreparation1458.Settle(store, lines.WaterLine, cancellationToken);
        for (int x = 0; x < width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (random.Next(13) != 0) continue;
            int y = height - 65;
            while ((At(store, x, y).LiquidAmount > 0 || At(store, x, y).IsActive) && y > height - 140) y--;
            runner.Run(x, y - random.Next(2, 5), random.Next(5, 30), 1000, Ash, true, 0, random.Next(1, 3), true);
            double scale = random.Next(1, 3);
            if (random.Next(3) == 0) scale *= 0.5;
            if (random.Next(2) == 0)
                runner.Run(x, y - random.Next(2, 5), (int)(random.Next(5, 15) * scale),
                    (int)(random.Next(10, 15) * scale), Ash, true, 1, 0.3);
            if (random.Next(2) == 0)
            {
                scale = random.Next(1, 3);
                runner.Run(x, y - random.Next(2, 5), (int)(random.Next(5, 15) * scale),
                    (int)(random.Next(10, 15) * scale), Ash, true, -1, 0.3);
            }
            runner.Run(x + random.Next(-10, 10), y + random.Next(-10, 10), random.Next(5, 15),
                random.Next(5, 10), LiquidCavity, false, random.Next(-1, 3), random.Next(-1, 3));
            if (random.Next(3) == 0)
                runner.Run(x + random.Next(-10, 10), y + random.Next(-10, 10), random.Next(10, 30),
                    random.Next(10, 20), LiquidCavity, false, random.Next(-1, 3), random.Next(-1, 3));
            if (random.Next(5) == 0)
                runner.Run(x + random.Next(-15, 15), y + random.Next(-15, 10), random.Next(15, 30),
                    random.Next(5, 20), LiquidCavity, false, random.Next(-1, 3), random.Next(-1, 3));
        }

        for (int i = 0; i < width; i++)
            runner.Run(random.Next(20, width - 20), random.Next(height - 180, height - 10),
                random.Next(2, 7), random.Next(2, 7), LiquidCavity);
        UnderworldLava1458.RestoreSurface(store, cancellationToken);
        int deposits = (int)((double)(width * height) * 0.0008);
        for (int i = 0; i < deposits; i++)
            runner.Run(random.Next(0, width), random.Next(height - 140, height), random.Next(2, 7), random.Next(3, 7), Hellstone);
    }

    internal static void CarveAndFill(WorldTileStore store, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        int height = store.Dimensions.HeightTiles, width = store.Dimensions.WidthTiles;
        int roof = height - random.Next(150, 190), basin = height - random.Next(40, 70);
        for (int x = 0; x < width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            roof = Math.Clamp(roof + random.Next(-3, 4), height - 190, height - 160);
            for (int y = roof - 20 - random.Next(3); y < height; y++)
            {
                ref WorldTile tile = ref At(store, x, y);
                if (y < roof) tile.Type = Ash;
                else
                {
                    tile.Flags &= ~WorldTileFlags.Active;
                    tile.LiquidAmount = 0;
                    ClearLavaFlag(ref tile);
                }
            }
        }
        for (int x = 10; x < width - 10; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            basin = Math.Min(basin + random.Next(-10, 11), height - 60);
            // The source jumps to height-120 here, not height-100. Do not replace this with a clamp.
            if (basin < height - 100) basin = height - 120;
            for (int y = basin; y < height - 10; y++)
            {
                ref WorldTile tile = ref At(store, x, y);
                if (tile.IsActive) continue;
                tile.LiquidAmount = byte.MaxValue;
                tile.LiquidKind = WorldLiquidKind.Lava;
            }
        }
    }

    // Tile.lava(false) clears only the lava bit: shimmer becomes honey, honey stays honey.
    internal static void ClearLavaFlag(ref WorldTile tile) => tile.LiquidKind = (WorldLiquidKind)((byte)tile.LiquidKind & 2);
    internal static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
