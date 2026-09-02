using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.Tests;

public sealed class WorldGenerationCandidateRunnerTests
{
    [Fact]
    public void Selected_generator_builds_isolated_candidate()
    {
        WorldGeneratorId generatorId = new("test:selected");
        var provider = new TestProvider(
            generatorId,
            static (request, builder) => builder.Add(
                new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:terrain")),
                new ActionPass(context =>
                {
                    var tile = new WorldGenerationTile(
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
                    Assert.True(context.Workspace.TrySetTile(3, 4, in tile));
                })));
        var service = new WorldGenerationCandidateRunner(new TestSource(provider));
        var request = new WorldGenerationRequest(generatorId, "Selected", 123, 32, 24);

        RuntimeWorldGenerationCandidateResult result = service.Generate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Candidate);
        Assert.NotNull(result.Execution);
        Assert.Equal(WorldGenerationExecutionStatus.Completed, result.Execution.Value.Status);
        Assert.True(result.Candidate.TryGetTile(3, 4, out WorldGenerationTile generated));
        Assert.True((generated.Flags & WorldGenerationTileFlags.Active) != 0);
    }

    [Fact]
    public void Unknown_generator_never_allocates_a_publishable_candidate()
    {
        WorldGeneratorId registered = new("test:registered");
        WorldGeneratorId missing = new("test:missing");
        var service = new WorldGenerationCandidateRunner(new TestSource(new TestProvider(registered)));
        var request = new WorldGenerationRequest(missing, "Missing", 1, 16, 16);

        RuntimeWorldGenerationCandidateResult result = service.Generate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeWorldGenerationCandidateStatus.GeneratorNotFound, result.Status);
        Assert.False(result.Succeeded);
        Assert.Null(result.Candidate);
        Assert.Null(result.Execution);
    }

    [Fact]
    public void Failed_pass_discards_partial_candidate()
    {
        WorldGeneratorId generatorId = new("test:failing");
        var provider = new TestProvider(
            generatorId,
            static (request, builder) => builder.Add(
                new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:explode")),
                new ActionPass(static _ => throw new InvalidOperationException("boom"))));
        var service = new WorldGenerationCandidateRunner(new TestSource(provider));
        var request = new WorldGenerationRequest(generatorId, "Failing", 1, 16, 16);

        RuntimeWorldGenerationCandidateResult result = service.Generate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeWorldGenerationCandidateStatus.GenerationFailed, result.Status);
        Assert.False(result.Succeeded);
        Assert.Null(result.Candidate);
        Assert.NotNull(result.Execution);
        Assert.Equal(WorldGenerationExecutionStatus.PassFailed, result.Execution.Value.Status);
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
        private readonly Action<WorldGenerationRequest, IWorldGenerationPlanBuilder>? build;

        public TestProvider(
            WorldGeneratorId id,
            Action<WorldGenerationRequest, IWorldGenerationPlanBuilder>? build = null)
        {
            Id = id;
            this.build = build;
        }

        public WorldGeneratorId Id { get; }

        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            build?.Invoke(request, builder);
    }

    private sealed class ActionPass : IWorldGenerationPass
    {
        private readonly Action<IWorldGenerationContext> execute;

        public ActionPass(Action<IWorldGenerationContext> execute) => this.execute = execute;

        public void Execute(IWorldGenerationContext context) => execute(context);
    }
}
