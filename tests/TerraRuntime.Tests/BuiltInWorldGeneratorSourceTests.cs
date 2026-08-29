using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class BuiltInWorldGeneratorSourceTests
{
    [Fact]
    public void Flat_generator_is_explicitly_registered_and_finalizes_complete_candidate()
    {
        BuiltInWorldGeneratorSource source = BuiltInWorldGeneratorSource.Instance;
        Assert.Contains(FlatWorldGenerationProvider.GeneratorId, source.CaptureWorldGeneratorIds().Span.ToArray());
        Assert.True(source.TryResolveWorldGenerator(FlatWorldGenerationProvider.GeneratorId, out var provider));
        Assert.NotNull(provider);

        var request = new WorldGenerationRequest(
            FlatWorldGenerationProvider.GeneratorId,
            "Flat",
            Seed: 123,
            WidthTiles: 16,
            HeightTiles: 12);
        var pipeline = new RuntimeWorldCreationPipeline(source);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Candidate);
        Assert.Equal(new WorldGenerationPoint(8, 3), result.Metadata.Spawn);
        Assert.Equal(new WorldGenerationPoint(2, 3), result.Metadata.Dungeon);
        Assert.Equal(new WorldGenerationLayers(4d, 7d), result.Metadata.Layers);

        Assert.True(result.Candidate.TryGetTile(8, 3, out WorldGenerationTile air));
        Assert.Equal(WorldGenerationTileFlags.None, air.Flags);

        Assert.True(result.Candidate.TryGetTile(8, 4, out WorldGenerationTile dirt));
        Assert.Equal((ushort)0, dirt.Type);
        Assert.True((dirt.Flags & WorldGenerationTileFlags.Active) != 0);

        Assert.True(result.Candidate.TryGetTile(8, 7, out WorldGenerationTile stone));
        Assert.Equal((ushort)1, stone.Type);
        Assert.True((stone.Flags & WorldGenerationTileFlags.Active) != 0);
    }

    [Fact]
    public void Flat_generator_rejects_world_too_short_for_required_layers()
    {
        var request = new WorldGenerationRequest(
            FlatWorldGenerationProvider.GeneratorId,
            "TooShort",
            Seed: 1,
            WidthTiles: 8,
            HeightTiles: 2);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(RuntimeWorldCreationPipelineStatus.GenerationFailed, result.Status);
        Assert.Null(result.Candidate);
    }
}
