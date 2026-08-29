using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldGenerationExecutorTests
{
    [Fact]
    public void Generator_registry_publishes_sorted_snapshot_and_lease_retires_registration()
    {
        var registry = new RuntimeWorldGeneratorRegistry();
        var z = new TestProvider("test:z");
        var a = new TestProvider("test:a");

        Assert.Equal(WorldGeneratorRegistrationResult.Registered, registry.TryRegister(z, out IWorldGeneratorRegistrationLease? zLease));
        Assert.Equal(WorldGeneratorRegistrationResult.Registered, registry.TryRegister(a, out IWorldGeneratorRegistrationLease? aLease));
        Assert.NotNull(zLease);
        Assert.NotNull(aLease);
        Assert.Equal(new[] { "test:a", "test:z" }, registry.Snapshot.Entries.Span.ToArray().Select(static entry => entry.Id.Value));
        Assert.True(registry.TryResolve(a.Id, out IWorldGenerationProvider? resolved));
        Assert.Same(a, resolved);

        aLease.Dispose();

        Assert.True(aLease.IsRetired);
        Assert.False(registry.TryResolve(a.Id, out _));
        Assert.True(registry.TryResolve(z.Id, out _));
        zLease.Dispose();
    }

    [Fact]
    public void Duplicate_generator_id_is_rejected_without_replacing_active_provider()
    {
        var registry = new RuntimeWorldGeneratorRegistry();
        var first = new TestProvider("test:same");
        var second = new TestProvider("test:same");

        Assert.Equal(WorldGeneratorRegistrationResult.Registered, registry.TryRegister(first, out IWorldGeneratorRegistrationLease? lease));
        Assert.Equal(WorldGeneratorRegistrationResult.DuplicateId, registry.TryRegister(second, out IWorldGeneratorRegistrationLease? duplicateLease));
        Assert.Null(duplicateLease);
        Assert.True(registry.TryResolve(first.Id, out IWorldGenerationProvider? resolved));
        Assert.Same(first, resolved);
        lease!.Dispose();
    }

    [Fact]
    public void Executor_orders_passes_by_dependencies_and_is_reproducible_for_fixed_seed()
    {
        var executionOrder = new List<string>();
        WorldGenerationPassId terrainId = new("test:terrain");
        WorldGenerationPassId decorateId = new("test:decorate");
        var provider = new TestProvider(
            "test:custom",
            (request, builder) =>
            {
                builder.Add(
                    new WorldGenerationPassDescriptor(decorateId, requiredAfter: [terrainId]),
                    new ActionPass(context =>
                    {
                        executionOrder.Add("decorate");
                        int x = context.Random.NextInt32(context.Workspace.WidthTiles);
                        Assert.True(context.Workspace.TrySetTile(
                            x,
                            2,
                            new WorldGenerationTile(0, 0, 0, 0, WorldGenerationTileFlags.Active, 0, 0, 0, 0, WorldGenerationLiquidKind.Water)));
                    }));
                builder.Add(
                    new WorldGenerationPassDescriptor(terrainId),
                    new ActionPass(context =>
                    {
                        executionOrder.Add("terrain");
                        int x = context.Random.NextInt32(context.Workspace.WidthTiles);
                        Assert.True(context.Workspace.TrySetTile(
                            x,
                            1,
                            new WorldGenerationTile(0, 0, 0, 0, WorldGenerationTileFlags.Active, 0, 0, 0, 0, WorldGenerationLiquidKind.Water)));
                    }));
            });
        var request = new WorldGenerationRequest(provider.Id, "Deterministic", 0x1234UL, 64, 32);
        var first = new RuntimeWorldGenerationWorkspace(request.WidthTiles, request.HeightTiles);
        var second = new RuntimeWorldGenerationWorkspace(request.WidthTiles, request.HeightTiles);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        WorldGenerationExecutionResult firstResult = RuntimeWorldGenerationExecutor.Execute(provider, in request, first, cancellationToken: cancellationToken);
        WorldGenerationExecutionResult secondResult = RuntimeWorldGenerationExecutor.Execute(provider, in request, second, cancellationToken: cancellationToken);

        Assert.Equal(WorldGenerationExecutionStatus.Completed, firstResult.Status);
        Assert.Equal(WorldGenerationExecutionStatus.Completed, secondResult.Status);
        Assert.Equal(new[] { "terrain", "decorate", "terrain", "decorate" }, executionOrder);
        Assert.Equal(FindActiveX(first, 1), FindActiveX(second, 1));
        Assert.Equal(FindActiveX(first, 2), FindActiveX(second, 2));
    }

    [Fact]
    public void Executor_rejects_duplicate_pass_id_transactionally()
    {
        WorldGenerationPassId id = new("test:duplicate");
        var provider = new TestProvider(
            "test:broken",
            (request, builder) =>
            {
                builder.Add(new WorldGenerationPassDescriptor(id), new ActionPass(static _ => { }));
                builder.Add(new WorldGenerationPassDescriptor(id), new ActionPass(static _ => { }));
            });
        var request = new WorldGenerationRequest(provider.Id, "Broken", 1, 16, 16);
        var workspace = new RuntimeWorldGenerationWorkspace(16, 16);

        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            workspace,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(WorldGenerationExecutionStatus.InvalidPlan, result.Status);
        Assert.Equal(id, result.PassId);
    }

    [Fact]
    public void Executor_reseeds_verified_vanilla_rng_for_each_pass()
    {
        var values = new List<int>();
        WorldGenerationPassId firstId = new("test:vanilla-rng-a");
        WorldGenerationPassId secondId = new("test:vanilla-rng-b");
        var provider = new TestProvider(
            "test:custom",
            (request, builder) =>
            {
                builder.Add(
                    new WorldGenerationPassDescriptor(firstId, WorldGenerationRngMode.VanillaSharedRng),
                    new ActionPass(context =>
                    {
                        Assert.NotNull(context.VanillaRandom);
                        values.Add(context.VanillaRandom!.Next());
                    }));
                builder.Add(
                    new WorldGenerationPassDescriptor(secondId, WorldGenerationRngMode.VanillaSharedRng, requiredAfter: [firstId]),
                    new ActionPass(context =>
                    {
                        Assert.NotNull(context.VanillaRandom);
                        values.Add(context.VanillaRandom!.Next());
                    }));
            });
        var request = new WorldGenerationRequest(provider.Id, "Rng", 1, 16, 16, SeedText: "123456");
        var workspace = new RuntimeWorldGenerationWorkspace(16, 16);

        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            workspace,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(WorldGenerationExecutionStatus.Completed, result.Status);
        Assert.Equal(2, values.Count);
        Assert.Equal(values[0], values[1]);
        var expected = new VanillaUnifiedRandom1458(123456);
        Assert.Equal(expected.Next(), values[0]);
    }

    [Fact]
    public void Executor_still_rejects_custom_provider_rng_without_contract()
    {
        bool executed = false;
        WorldGenerationPassId id = new("test:custom-rng");
        var provider = new TestProvider(
            "test:custom",
            (request, builder) => builder.Add(
                new WorldGenerationPassDescriptor(id, WorldGenerationRngMode.CustomProviderRng),
                new ActionPass(_ => executed = true)));
        var request = new WorldGenerationRequest(provider.Id, "Rng", 1, 16, 16);
        var workspace = new RuntimeWorldGenerationWorkspace(16, 16);

        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            workspace,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(WorldGenerationExecutionStatus.UnsupportedRngMode, result.Status);
        Assert.Equal(id, result.PassId);
        Assert.False(executed);
    }

    [Fact]
    public void Executor_honors_cancellation_before_provider_execution()
    {
        bool providerCalled = false;
        var provider = new TestProvider(
            "test:cancel",
            (request, builder) =>
            {
                providerCalled = true;
                builder.Add(
                    new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:pass")),
                    new ActionPass(static _ => { }));
            });
        var request = new WorldGenerationRequest(provider.Id, "Cancelled", 1, 16, 16);
        var workspace = new RuntimeWorldGenerationWorkspace(16, 16);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            workspace,
            cancellationToken: cancellation.Token);

        Assert.Equal(WorldGenerationExecutionStatus.Cancelled, result.Status);
        Assert.False(providerCalled);
    }

    [Fact]
    public void Progress_callbacks_are_bounded_per_pass()
    {
        WorldGenerationPassId id = new("test:progress");
        var provider = new TestProvider(
            "test:progress-provider",
            (request, builder) => builder.Add(
                new WorldGenerationPassDescriptor(id),
                new ActionPass(context =>
                {
                    for (int i = 0; i < RuntimeWorldGenerationExecutor.MaxProgressReportsPerPass + 100; i++)
                        context.ReportProgress(0.5d, "working");
                })));
        var request = new WorldGenerationRequest(provider.Id, "Progress", 1, 16, 16);
        var workspace = new RuntimeWorldGenerationWorkspace(16, 16);
        var progress = new CountingProgressSink();

        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            workspace,
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldGenerationExecutionStatus.Completed, result.Status);
        Assert.Equal(RuntimeWorldGenerationExecutor.MaxProgressReportsPerPass, progress.Count);
    }

    [Fact]
    public void Workspace_rejects_unknown_official_client_tile_id_and_out_of_bounds_writes()
    {
        var workspace = new RuntimeWorldGenerationWorkspace(16, 16);
        var invalid = new WorldGenerationTile(
            Type: (ushort)VanillaTileIds.Count,
            Wall: 0,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.Active,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);

        Assert.False(workspace.TrySetTile(1, 1, in invalid));
        Assert.False(workspace.TrySetTile(-1, 1, default));
        Assert.False(workspace.TrySetTile(16, 1, default));
    }

    private static int FindActiveX(RuntimeWorldGenerationWorkspace workspace, int y)
    {
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            Assert.True(workspace.TryGetTile(x, y, out WorldGenerationTile tile));
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                return x;
        }

        return -1;
    }

    private sealed class TestProvider : IWorldGenerationProvider
    {
        private readonly Action<WorldGenerationRequest, IWorldGenerationPlanBuilder>? build;

        public TestProvider(string id, Action<WorldGenerationRequest, IWorldGenerationPlanBuilder>? build = null)
        {
            Id = new WorldGeneratorId(id);
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

    private sealed class CountingProgressSink : IWorldGenerationProgressSink
    {
        public int Count { get; private set; }

        public void Report(in WorldGenerationProgress progress) => Count++;
    }
}
