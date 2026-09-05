using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class FinalizerTests
{
    [Fact]
    public void Finalizer_rejects_candidate_without_spawn()
    {
        var candidate = new Workspace(100, 60);
        Assert.True(candidate.TrySetDungeon(10, 25));
        Assert.True(candidate.TrySetLayers(18d, 34d));

        FinalizationResult result = Finalizer.Finalize(candidate);

        Assert.Equal(FinalizationStatus.MissingSpawn, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Finalizer_rejects_candidate_without_dungeon()
    {
        var candidate = new Workspace(100, 60);
        Assert.True(candidate.TrySetSpawn(50, 20));
        Assert.True(candidate.TrySetLayers(18d, 34d));

        FinalizationResult result = Finalizer.Finalize(candidate);

        Assert.Equal(FinalizationStatus.MissingDungeon, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Finalizer_rejects_candidate_without_layers()
    {
        var candidate = new Workspace(100, 60);
        Assert.True(candidate.TrySetSpawn(50, 20));
        Assert.True(candidate.TrySetDungeon(10, 25));

        FinalizationResult result = Finalizer.Finalize(candidate);

        Assert.Equal(FinalizationStatus.MissingLayers, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Finalizer_captures_immutable_semantic_snapshot()
    {
        var candidate = new Workspace(100, 60);
        Assert.True(candidate.TrySetSpawn(50, 20));
        Assert.True(candidate.TrySetDungeon(10, 25));
        Assert.True(candidate.TrySetLayers(18d, 34d));

        FinalizationResult result = Finalizer.Finalize(candidate);

        Assert.True(result.Succeeded);
        Assert.Equal(FinalizationStatus.Finalized, result.Status);
        Assert.Equal(new WorldGenerationPoint(50, 20), result.Metadata.Spawn);
        Assert.Equal(new WorldGenerationPoint(10, 25), result.Metadata.Dungeon);
        Assert.Equal(new WorldGenerationLayers(18d, 34d), result.Metadata.Layers);

        Assert.True(candidate.TrySetSpawn(51, 21));
        Assert.Equal(new WorldGenerationPoint(50, 20), result.Metadata.Spawn);
    }
}
