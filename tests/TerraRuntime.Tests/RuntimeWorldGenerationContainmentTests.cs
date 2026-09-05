using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldGenerationContainmentTests
{
    [Fact]
    public void Startup_validation_policy_does_not_infer_vanilla_completeness_from_dimensions()
    {
        Assert.Equal(
            ValidationMode.GenericStructural,
            RuntimeWorldCreationPipeline.ResolveValidationMode(new WorldGeneratorId("fixture:worldgen")));
        Assert.Equal(
            ValidationMode.GenericStructural,
            RuntimeWorldCreationPipeline.ResolveValidationMode(FlatProvider.GeneratorId));
        Assert.Equal(
            ValidationMode.GenericStructural,
            RuntimeWorldCreationPipeline.ResolveValidationMode(SkyblockProvider.GeneratorId));
        Assert.Equal(
            ValidationMode.VanillaComplete,
            RuntimeWorldCreationPipeline.ResolveValidationMode(Provider1458.GeneratorId));
        Assert.Equal(
            ValidationMode.VanillaComplete,
            RuntimeWorldCreationPipeline.ResolveValidationMode(SurfaceDecorationProvider.GeneratorId));
    }

    [Fact]
    public void Throw_after_partial_workspace_mutation_never_publishes_world()
    {
        string root = CreateRoot();
        string worldPath = Path.Combine(root, "throwing.wld");
        var generatorId = new WorldGeneratorId("test:throw-after-write");
        var provider = new TestProvider(generatorId, static (_, builder) =>
            builder.Add(
                new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:partial-write")),
                new ActionPass(static context =>
                {
                    WorldGenerationTile marker = MarkerTile();
                    Assert.True(context.Workspace.TrySetTile(1, 1, in marker));
                    throw new InvalidOperationException("injected worldgen failure after mutation");
                })));

        try
        {
            RuntimeWorldCreationPersistenceResult result = CreatePipeline(provider).TryCreateAndPersist(
                CreateRequest(generatorId),
                worldPath,
                Guid.Parse("11111111-2222-4333-8444-555555555555"),
                worldId: 1458,
                creationTimeBinary: 0,
                lastPlayedBinary: 0,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(RuntimeWorldCreationPersistenceStatus.GenerationFailed, result.Status);
            Assert.False(result.Succeeded);
            Assert.False(File.Exists(worldPath));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
            Assert.Equal(WorldGenerationExecutionStatus.PassFailed, result.Creation?.Generation.Execution?.Status);
            InvalidOperationException error = Assert.IsType<InvalidOperationException>(result.Creation?.Generation.Execution?.Error);
            Assert.Contains("injected worldgen failure", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Cancellation_after_partial_workspace_mutation_never_publishes_world()
    {
        string root = CreateRoot();
        string worldPath = Path.Combine(root, "cancelled.wld");
        using var cancellation = new CancellationTokenSource();
        var generatorId = new WorldGeneratorId("test:cancel-after-write");
        var provider = new TestProvider(generatorId, (_, builder) =>
            builder.Add(
                new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:partial-write-cancel")),
                new ActionPass(context =>
                {
                    WorldGenerationTile marker = MarkerTile();
                    Assert.True(context.Workspace.TrySetTile(1, 1, in marker));
                    cancellation.Cancel();
                    context.CancellationToken.ThrowIfCancellationRequested();
                })));

        try
        {
            RuntimeWorldCreationPersistenceResult result = CreatePipeline(provider).TryCreateAndPersist(
                CreateRequest(generatorId),
                worldPath,
                Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"),
                worldId: 1459,
                creationTimeBinary: 0,
                lastPlayedBinary: 0,
                cancellationToken: cancellation.Token);

            Assert.Equal(RuntimeWorldCreationPersistenceStatus.GenerationFailed, result.Status);
            Assert.False(result.Succeeded);
            Assert.False(File.Exists(worldPath));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
            Assert.Equal(WorldGenerationExecutionStatus.Cancelled, result.Creation?.Generation.Execution?.Status);
            Assert.Null(result.Creation?.Generation.Execution?.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimeWorldCreationPersistencePipeline CreatePipeline(IWorldGenerationProvider provider) =>
        new(new TestSource(provider), maxTileCount: 1_000_000);

    private static WorldGenerationRequest CreateRequest(WorldGeneratorId generatorId) =>
        new(generatorId, "Containment", Seed: 1458, WidthTiles: 32, HeightTiles: 24);

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"terraruntime-worldgen-containment-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static WorldGenerationTile MarkerTile() =>
        new(
            Type: 0,
            Wall: 0,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.Active,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);

    private sealed class TestSource : ITerraRuntimeWorldGeneratorSource
    {
        private readonly IWorldGenerationProvider provider;

        public TestSource(IWorldGenerationProvider provider) => this.provider = provider;

        public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds() => new[] { provider.Id };

        public bool TryResolveWorldGenerator(WorldGeneratorId id, out IWorldGenerationProvider? resolved)
        {
            if (id == provider.Id)
            {
                resolved = provider;
                return true;
            }

            resolved = null;
            return false;
        }
    }

    private sealed class TestProvider : IWorldGenerationProvider
    {
        private readonly Action<WorldGenerationRequest, IWorldGenerationPlanBuilder> build;

        public TestProvider(
            WorldGeneratorId id,
            Action<WorldGenerationRequest, IWorldGenerationPlanBuilder> build)
        {
            Id = id;
            this.build = build;
        }

        public WorldGeneratorId Id { get; }

        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            build(request, builder);
    }

    private sealed class ActionPass : IWorldGenerationPass
    {
        private readonly Action<IWorldGenerationContext> action;

        public ActionPass(Action<IWorldGenerationContext> action) => this.action = action;

        public void Execute(IWorldGenerationContext context) => action(context);
    }
}
