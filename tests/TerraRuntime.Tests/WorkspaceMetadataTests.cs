using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorkspaceMetadataTests
{
    [Fact]
    public void Metadata_workspace_validates_anchors_and_layers_transactionally()
    {
        var workspace = new Workspace(100, 60);

        Assert.True(workspace.TrySetSpawn(50, 20));
        Assert.True(workspace.TrySetDungeon(10, 30));
        Assert.True(workspace.TrySetLayers(18.5d, 36.25d));

        Assert.False(workspace.TrySetSpawn(100, 20));
        Assert.False(workspace.TrySetDungeon(-1, 30));
        Assert.False(workspace.TrySetLayers(0d, 36.25d));
        Assert.False(workspace.TrySetLayers(40d, 30d));
        Assert.False(workspace.TrySetLayers(double.NaN, 36.25d));

        Assert.True(workspace.TryGetSpawn(out WorldGenerationPoint spawn));
        Assert.Equal(new WorldGenerationPoint(50, 20), spawn);
        Assert.True(workspace.TryGetDungeon(out WorldGenerationPoint dungeon));
        Assert.Equal(new WorldGenerationPoint(10, 30), dungeon);
        Assert.True(workspace.TryGetLayers(out WorldGenerationLayers layers));
        Assert.Equal(new WorldGenerationLayers(18.5d, 36.25d), layers);
    }

    [Fact]
    public void Executor_exposes_runtime_owned_metadata_surface_to_custom_passes()
    {
        var provider = new MetadataProvider();
        var request = new WorldGenerationRequest(provider.Id, "Metadata", 123UL, 100, 60);
        var workspace = new Workspace(request.WidthTiles, request.HeightTiles);

        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            workspace,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(WorldGenerationExecutionStatus.Completed, result.Status);
        Assert.True(workspace.TryGetSpawn(out WorldGenerationPoint spawn));
        Assert.Equal(new WorldGenerationPoint(50, 20), spawn);
        Assert.True(workspace.TryGetDungeon(out WorldGenerationPoint dungeon));
        Assert.Equal(new WorldGenerationPoint(12, 25), dungeon);
        Assert.True(workspace.TryGetLayers(out WorldGenerationLayers layers));
        Assert.Equal(new WorldGenerationLayers(18d, 34d), layers);
    }

    private sealed class MetadataProvider : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => new("test:metadata");

        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
        {
            builder.Add(
                new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:metadata-pass")),
                new MetadataPass());
        }
    }

    private sealed class MetadataPass : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Production generation workspace must expose semantic metadata.");

            if (!metadata.TrySetSpawn(50, 20) ||
                !metadata.TrySetDungeon(12, 25) ||
                !metadata.TrySetLayers(18d, 34d))
            {
                throw new InvalidOperationException("Fixture metadata must be valid for the requested world.");
            }
        }
    }
}
