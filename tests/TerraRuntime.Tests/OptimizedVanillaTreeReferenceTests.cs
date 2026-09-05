using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedVanillaTreeReferenceTests
{
    [Fact]
    public void Optimized_ordinary_trees_use_source_backed_vanilla_growth_frames()
    {
        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
            "Optimized vanilla tree reference",
            Seed: 0x5EEDC0DEUL,
            WidthTiles: 640,
            HeightTiles: 320);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            $"{result.Status} execution={result.Generation.Execution?.Status} " +
            $"pass={result.Generation.Execution?.PassId.Value} err={result.Generation.Execution?.Error}");
        Workspace world = Assert.IsType<Workspace>(result.Candidate);

        var frames = new HashSet<(short X, short Y)>();
        int treeTiles = 0;
        for (int x = 0; x < world.WidthTiles; x++)
        for (int y = 0; y < world.HeightTiles; y++)
        {
            if (!world.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0 ||
                tile.Type != VanillaTileIds.Trees.Value)
            {
                continue;
            }

            treeTiles++;
            frames.Add((tile.FrameX, tile.FrameY));
        }

        Assert.True(treeTiles >= 120, $"Expected the optimized tree density budget, found only {treeTiles} tree tiles.");
        Assert.True(frames.Count >= 12, $"Expected source-backed GrowTree frame diversity, found only {frames.Count} unique frames.");
        Assert.Contains(frames, IsSourceBackedTopFrame);
        Assert.Contains(frames, IsSourceBackedRootFrame);
    }

    private static bool IsSourceBackedTopFrame((short X, short Y) frame)
    {
        for (int variant = 0; variant < 3; variant++)
        {
            TreeFrame1458 leafy = TreeFrameCatalog1458.Top(leafy: true, variant);
            TreeFrame1458 bare = TreeFrameCatalog1458.Top(leafy: false, variant);
            if (frame == (leafy.X, leafy.Y) || frame == (bare.X, bare.Y))
                return true;
        }
        return false;
    }

    private static bool IsSourceBackedRootFrame((short X, short Y) frame)
    {
        for (int variant = 0; variant < 3; variant++)
        {
            TreeFrame1458 left = TreeFrameCatalog1458.LeftRoot(variant);
            TreeFrame1458 right = TreeFrameCatalog1458.RightRoot(variant);
            if (frame == (left.X, left.Y) || frame == (right.X, right.Y))
                return true;
        }
        return false;
    }
}
