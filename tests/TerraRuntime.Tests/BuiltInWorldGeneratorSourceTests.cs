using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

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
    public void Vanilla_and_skyblock_generators_are_explicitly_registered()
    {
        BuiltInWorldGeneratorSource source = BuiltInWorldGeneratorSource.Instance;
        Assert.Equal(3, source.CaptureWorldGeneratorIds().Length);
        Assert.Contains(VanillaWorldGenerationProvider1458.GeneratorId, source.CaptureWorldGeneratorIds().Span.ToArray());
        Assert.Contains(SkyblockWorldGenerationProvider.GeneratorId, source.CaptureWorldGeneratorIds().Span.ToArray());
        Assert.True(source.TryResolveWorldGenerator(VanillaWorldGenerationProvider1458.GeneratorId, out var provider));
        Assert.NotNull(provider);
        Assert.True(source.TryResolveWorldGenerator(SkyblockWorldGenerationProvider.GeneratorId, out var skyblock));
        Assert.NotNull(skyblock);

        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Vanilla",
            Seed: 8675309,
            WidthTiles: 192,
            HeightTiles: 128)
        {
            SeedText = "8675309"
        };
        var pipeline = new RuntimeWorldCreationPipeline(source);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.True(result.Metadata.Layers.WorldSurface > 0d);
        Assert.True(result.Metadata.Layers.RockLayer > result.Metadata.Layers.WorldSurface);
        Assert.True(result.Metadata.VanillaSeedProfile.IsDefault);

        var surfaces = new HashSet<int>();
        for (int x = 16; x < request.WidthTiles - 16; x += 8)
            surfaces.Add(FindSurface(result.Candidate!, x));
        Assert.True(surfaces.Count > 1);
    }

    [Fact]
    public void Vanilla_generator_carries_special_and_secret_seed_profile_into_finalization()
    {
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Secret",
            Seed: 123,
            WidthTiles: 160,
            HeightTiles: 112)
        {
            SeedText = "get fixed boi|planetoids|bring a towel"
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, $"{result.Status} gen={result.Generation.Status} fin={result.Finalization?.Status} validation={result.Finalization?.Validation} err={result.Generation.Execution?.Error}");
        Assert.True(result.Metadata.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.Zenith));
        Assert.True(result.Metadata.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy));
        Assert.True(result.Metadata.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.Planetoids));
        Assert.True(result.Metadata.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.BringATowel));
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

    private static int FindSurface(RuntimeWorldGenerationWorkspace workspace, int x)
    {
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            Assert.True(workspace.TryGetTile(x, y, out WorldGenerationTile tile));
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                return y;
        }

        return workspace.HeightTiles;
    }
}
