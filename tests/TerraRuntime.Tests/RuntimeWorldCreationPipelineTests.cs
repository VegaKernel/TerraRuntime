using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldCreationPipelineTests
{
    [Fact]
    public void Complete_generator_candidate_is_ready_for_persistence()
    {
        WorldGeneratorId generatorId = new("test:complete");
        var provider = new TestProvider(generatorId, static (_, builder) => builder.Add(
            new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:metadata")),
            new ActionPass(static context =>
            {
                Assert.NotNull(context.Metadata);
                Assert.True(context.Metadata.TrySetSpawn(10, 8));
                Assert.True(context.Metadata.TrySetDungeon(2, 9));
                Assert.True(context.Metadata.TrySetLayers(6d, 12d));
            })));
        var pipeline = new RuntimeWorldCreationPipeline(new TestSource(provider));
        var request = new WorldGenerationRequest(generatorId, "Complete", 7, 32, 24);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(RuntimeWorldCreationPipelineStatus.ReadyToPersist, result.Status);
        Assert.NotNull(result.Candidate);
        Assert.True(result.Finalization.HasValue);
        Assert.True(result.Finalization.Value.Succeeded);
        Assert.Equal(new WorldGenerationPoint(10, 8), result.Metadata.Spawn);
        Assert.Equal(new WorldGenerationPoint(2, 9), result.Metadata.Dungeon);
        Assert.Equal(new WorldGenerationLayers(6d, 12d), result.Metadata.Layers);
    }

    [Fact]
    public void Missing_semantic_anchor_discards_otherwise_successful_candidate()
    {
        WorldGeneratorId generatorId = new("test:incomplete");
        var provider = new TestProvider(generatorId, static (_, builder) => builder.Add(
            new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:metadata")),
            new ActionPass(static context =>
            {
                Assert.NotNull(context.Metadata);
                Assert.True(context.Metadata.TrySetSpawn(10, 8));
                Assert.True(context.Metadata.TrySetLayers(6d, 12d));
            })));
        var pipeline = new RuntimeWorldCreationPipeline(new TestSource(provider));
        var request = new WorldGenerationRequest(generatorId, "Incomplete", 7, 32, 24);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(RuntimeWorldCreationPipelineStatus.FinalizationFailed, result.Status);
        Assert.Null(result.Candidate);
        Assert.True(result.Generation.Succeeded);
        Assert.Equal(FinalizationStatus.MissingDungeon, result.Finalization?.Status);
    }

    [Fact]
    public void Generator_failure_never_reaches_finalization()
    {
        WorldGeneratorId generatorId = new("test:failing");
        var provider = new TestProvider(generatorId, static (_, builder) => builder.Add(
            new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:failure")),
            new ActionPass(static _ => throw new InvalidOperationException("boom"))));
        var pipeline = new RuntimeWorldCreationPipeline(new TestSource(provider));
        var request = new WorldGenerationRequest(generatorId, "Failing", 7, 32, 24);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(RuntimeWorldCreationPipelineStatus.GenerationFailed, result.Status);
        Assert.Null(result.Candidate);
        Assert.False(result.Generation.Succeeded);
        Assert.Null(result.Finalization);
    }

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
