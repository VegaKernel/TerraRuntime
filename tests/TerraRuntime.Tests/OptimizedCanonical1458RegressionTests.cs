using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedCanonical1458RegressionTests
{
    [Fact]
    public void Optimized_canonical_small_seed_1458_satisfies_landmark_budget()
    {
        var request = new WorldGenerationRequest(
            OptimizedWorldGenerationProvider.GeneratorId,
            "Optimized canonical Small seed 1458",
            Seed: 1458UL,
            WidthTiles: 4200,
            HeightTiles: 1200);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            $"{result.Status} gen={result.Generation.Status} execution={result.Generation.Execution?.Status} " +
            $"pass={result.Generation.Execution?.PassId.Value} err={result.Generation.Execution?.Error}");
        RuntimeWorldGenerationWorkspace world = Assert.IsType<RuntimeWorldGenerationWorkspace>(result.Candidate);

        WorldChest[] generated = world.CaptureGeneratedChests();
        Assert.Equal(4, generated.Count(static chest => chest.Name.StartsWith("Sky Cache ", StringComparison.Ordinal)));

        int skyWaterCells = 0;
        int skyBottom = Math.Max(1, (int)Math.Floor(result.Metadata.Layers.WorldSurface) - 18);
        for (int x = 0; x < world.WidthTiles; x++)
        for (int y = 0; y < skyBottom; y++)
        {
            if (world.TryGetTile(x, y, out WorldGenerationTile tile) &&
                tile.LiquidAmount > 0 &&
                tile.LiquidKind == WorldGenerationLiquidKind.Water)
            {
                skyWaterCells++;
            }
        }

        Assert.True(skyWaterCells >= 80, $"Expected four explicit floating lakes, found only {skyWaterCells} sky water cells.");
    }
}
