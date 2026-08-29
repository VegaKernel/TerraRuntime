using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldGenerationFinalizerTests
{
    [Fact]
    public void Finalizer_rejects_candidate_without_spawn()
    {
        var candidate = new RuntimeWorldGenerationWorkspace(100, 60);
        Assert.True(candidate.TrySetDungeon(10, 25));
        Assert.True(candidate.TrySetLayers(18d, 34d));

        RuntimeWorldGenerationFinalizationResult result = RuntimeWorldGenerationFinalizer.Finalize(candidate);

        Assert.Equal(RuntimeWorldGenerationFinalizationStatus.MissingSpawn, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Finalizer_rejects_candidate_without_dungeon()
    {
        var candidate = new RuntimeWorldGenerationWorkspace(100, 60);
        Assert.True(candidate.TrySetSpawn(50, 20));
        Assert.True(candidate.TrySetLayers(18d, 34d));

        RuntimeWorldGenerationFinalizationResult result = RuntimeWorldGenerationFinalizer.Finalize(candidate);

        Assert.Equal(RuntimeWorldGenerationFinalizationStatus.MissingDungeon, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Finalizer_rejects_candidate_without_layers()
    {
        var candidate = new RuntimeWorldGenerationWorkspace(100, 60);
        Assert.True(candidate.TrySetSpawn(50, 20));
        Assert.True(candidate.TrySetDungeon(10, 25));

        RuntimeWorldGenerationFinalizationResult result = RuntimeWorldGenerationFinalizer.Finalize(candidate);

        Assert.Equal(RuntimeWorldGenerationFinalizationStatus.MissingLayers, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Finalizer_captures_immutable_semantic_snapshot()
    {
        var candidate = new RuntimeWorldGenerationWorkspace(100, 60);
        Assert.True(candidate.TrySetSpawn(50, 20));
        Assert.True(candidate.TrySetDungeon(10, 25));
        Assert.True(candidate.TrySetLayers(18d, 34d));

        RuntimeWorldGenerationFinalizationResult result = RuntimeWorldGenerationFinalizer.Finalize(candidate);

        Assert.True(result.Succeeded);
        Assert.Equal(RuntimeWorldGenerationFinalizationStatus.Finalized, result.Status);
        Assert.Equal(new WorldGenerationPoint(50, 20), result.Metadata.Spawn);
        Assert.Equal(new WorldGenerationPoint(10, 25), result.Metadata.Dungeon);
        Assert.Equal(new WorldGenerationLayers(18d, 34d), result.Metadata.Layers);

        Assert.True(candidate.TrySetSpawn(51, 21));
        Assert.Equal(new WorldGenerationPoint(50, 20), result.Metadata.Spawn);
    }
}
