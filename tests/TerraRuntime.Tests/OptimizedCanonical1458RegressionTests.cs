using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedCanonical1458RegressionTests
{
    [Fact]
    public void Optimized_canonical_small_seed_1458_satisfies_landmark_budget()
    {
        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
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
        Workspace world = Assert.IsType<Workspace>(result.Candidate);

        WorldChest[] generated = world.CaptureGeneratedChests();
        Assert.Equal(4, generated.Count(static chest => chest.Name.StartsWith("Sky Cache ", StringComparison.Ordinal)));

        int skyWaterCells = 0;
        int exposedHorizontalLakeEdges = 0;
        int skyBottom = Math.Max(1, (int)Math.Floor(result.Metadata.Layers.WorldSurface) - 18);
        for (int x = 1; x < world.WidthTiles - 1; x++)
        for (int y = 0; y < skyBottom; y++)
        {
            if (!world.TryGetTile(x, y, out WorldGenerationTile tile) ||
                tile.LiquidAmount == 0 ||
                tile.LiquidKind != WorldGenerationLiquidKind.Water)
            {
                continue;
            }

            skyWaterCells++;
            if (IsOpenHorizontalEdge(world, x - 1, y) || IsOpenHorizontalEdge(world, x + 1, y))
                exposedHorizontalLakeEdges++;
        }

        Assert.True(skyWaterCells >= 54, $"Expected three explicit floating lakes, found only {skyWaterCells} sky water cells.");
        Assert.Equal(0, exposedHorizontalLakeEdges);
    }

    private static bool IsOpenHorizontalEdge(Workspace world, int x, int y)
    {
        Assert.True(world.TryGetTile(x, y, out WorldGenerationTile neighbour));
        return (neighbour.Flags & WorldGenerationTileFlags.Active) == 0 && neighbour.LiquidAmount == 0;
    }
}
