using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary AddPasses DirtToMud/Silt/OresAndShinies scheduling, pinned to 1.4.5.8.
/// Owns population and draw order only; all brushes use the existing small TileRunner.</summary>
internal static class MineralDeposits1458
{
    public static void Apply(MidStage1458 stage, IWorldGenerationContext context, Workspace workspace)
    {
        var terrain = workspace.VanillaTerrainState ?? throw new InvalidOperationException("Deposits require Terrain state.");
        var lines = workspace.VanillaLiquidLines ?? throw new InvalidOperationException("Deposits require liquid lines.");
        var bootstrap = workspace.VanillaBootstrapState ?? throw new InvalidOperationException("Deposits require Reset state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out var layers))
            throw new InvalidOperationException("Deposits require world layers.");
        context.CancellationToken.ThrowIfCancellationRequested();
        var random = context.VanillaRandom ?? throw new InvalidOperationException("Deposits require shared vanilla RNG.");
        int width = workspace.WidthTiles, height = workspace.HeightTiles, area = checked(width * height);
        var runner = new SmallTerrainRunner1458(workspace.TileStore, random, layers.WorldSurface, lines,
            context.CancellationToken, rockLayer: layers.RockLayer);
        switch (stage)
        {
            case MidStage1458.DirtToMud:
                // Source compares int iteration against a double population (ceiling, not truncation).
                for (int i = 0; i < area * 0.001; i++)
                    runner.Run(random.Next(width), random.Next((int)terrain.RockLayerLow, height),
                        random.Next(2, 6), random.Next(2, 40), SmallTerrainRunner1458.Mud, ignoreTileType: 53);
                break;
            case MidStage1458.Silt:
                SiltBand((int)(area * 0.0001f), 5, 12, 15, 50);
                SiltBand((int)(area * 0.0005f), 2, 5, 2, 5);
                break;
            case MidStage1458.Shinies:
                int surfaceLow = (int)terrain.WorldSurfaceLow, surfaceHigh = (int)terrain.WorldSurfaceHigh;
                int rockLow = (int)terrain.RockLayerLow, rockHigh = (int)terrain.RockLayerHigh;
                Ore(bootstrap.CopperOre, .00006, surfaceLow, surfaceHigh, 3, 6, 2, 6);
                Ore(bootstrap.CopperOre, .00008, surfaceHigh, rockHigh, 3, 7, 3, 7);
                Ore(bootstrap.CopperOre, .0002, rockLow, height, 4, 9, 4, 8);
                Ore(bootstrap.IronOre, .00003, surfaceLow, surfaceHigh, 3, 7, 2, 5);
                Ore(bootstrap.IronOre, .00008, surfaceHigh, rockHigh, 3, 6, 3, 6);
                Ore(bootstrap.IronOre, .0002, rockLow, height, 4, 9, 4, 8);
                Ore(bootstrap.SilverOre, .000026, surfaceHigh, rockHigh, 3, 6, 3, 6);
                Ore(bootstrap.SilverOre, .00015, rockLow, height, 4, 9, 4, 8);
                Ore(bootstrap.GoldOre, .00012, rockLow, height, 4, 8, 4, 8);
                Ore(bootstrap.SilverOre, .00017, 0, surfaceLow, 4, 9, 4, 8);
                Ore(bootstrap.GoldOre, .00012, 0, surfaceLow - 20, 4, 8, 4, 8);
                Ore(context.Request.Options.Evil == WorldGenerationEvil.Crimson ? 204 : 22,
                    .0000225, (int)layers.RockLayer, height, 3, 6, 4, 8);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage));
        }

        void SiltBand(int count, int minStrength, int maxStrength, int minSteps, int maxSteps)
        {
            for (int i = 0; i < count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                int x = random.Next(width), y = random.Next((int)terrain.RockLayerHigh, height);
                // Rejection precedes strength/steps draws, including for inactive sampled cells.
                if (workspace.TileStore.Get(x, y).Wall is 187 or 216) continue;
                runner.Run(x, y, random.Next(minStrength, maxStrength), random.Next(minSteps, maxSteps), 123);
            }
        }

        void Ore(int type, double density, int minY, int maxY, int minStrength, int maxStrength, int minSteps, int maxSteps)
        {
            int count = (int)(area * density);
            for (int i = 0; i < count; i++)
                runner.Run(random.Next(width), random.Next(minY, maxY),
                    random.Next(minStrength, maxStrength), random.Next(minSteps, maxSteps), type);
        }
    }
}
