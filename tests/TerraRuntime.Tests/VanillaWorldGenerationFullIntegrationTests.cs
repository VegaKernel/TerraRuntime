using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGenerationFullIntegrationTests
{
    private static readonly WorldGeneratorId VanillaId = new("terraruntime:vanilla");

    [Fact]
    public void Canonical_passes_preserve_registered_chest_anchors()
    {
        var request = new WorldGenerationRequest(VanillaId, "Chest anchors", 42, 4200, 1200) { SeedText = "42" };
        var workspace = new Workspace(request.WidthTiles, request.HeightTiles);
        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            new ChestCheckingProvider(), in request, workspace,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Error?.ToString());
    }

    private sealed class ChestCheckingProvider : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => VanillaId;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            new SourceBackedFinal1458().BuildPlan(in request, new ChestCheckingBuilder(builder));
    }

    private sealed class ChestCheckingBuilder(IWorldGenerationPlanBuilder inner) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            inner.Add(descriptor, new ChestCheckingPass(descriptor.Id, pass));
    }

    private sealed class ChestCheckingPass(WorldGenerationPassId id, IWorldGenerationPass inner) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            inner.Execute(context);
            var workspace = Assert.IsType<Workspace>(context.Workspace);
            foreach (WorldChest chest in workspace.CaptureGeneratedChests())
            {
                WorldTile tile = workspace.TileStore.Get(chest.X, chest.Y);
                Assert.True(tile.IsActive && tile.Type is 21 or 467,
                    $"Pass {id} damaged chest ({chest.X},{chest.Y}): type={tile.Type}, active={tile.IsActive}.");
            }
        }
    }


    [Fact]
    public void Built_in_source_resolves_vanilla_provider()
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        IWorldGenerationProvider? resolved = GetVanillaProvider(source, VanillaId);
        Assert.NotNull(resolved);
        Assert.Equal(VanillaId, resolved.Id);
    }

    [Fact]
    public void Canonical_small_generation_produces_valid_metadata_and_terrain()
    {
        var request = new WorldGenerationRequest(VanillaId, "Canonical", 42, 4200, 1200)
        {
            SeedText = "42"
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.NotNull(result.Generation.Execution);
        Assert.Equal(WorldGenerationExecutionStatus.Completed, result.Generation.Execution.Value.Status);
        Assert.True(result.Candidate!.TryGetSpawn(out WorldGenerationPoint spawn));
        Assert.InRange(spawn.X, 0, request.WidthTiles - 1);
        Assert.InRange(spawn.Y, 0, request.HeightTiles - 1);
        Assert.True(result.Candidate.TryGetLayers(out WorldGenerationLayers layers));
        Assert.True(layers.WorldSurface > 0d);
        Assert.True(layers.RockLayer > layers.WorldSurface);
        AssertSourceShapedTerrain(result.Candidate);
        AssertSourceFramedTrees(result.Candidate);
        DungeonGraph1458 graph = Assert.IsType<DungeonGraph1458>(result.Candidate.VanillaDungeonGraph);
        Assert.InRange(graph.RoomCount, 3, 40);
        Assert.InRange(graph.HallCount, 45, 120);
        Assert.True(graph.HorizontalHallCount > 0);
        Assert.True(graph.VerticalHallCount > 0);
        Assert.True(graph.Bounds.Width >= 120, $"Dungeon graph width was only {graph.Bounds.Width} tiles.");
        Assert.True(graph.Bounds.Height >= 120, $"Dungeon graph height was only {graph.Bounds.Height} tiles.");
        Assert.Contains(graph.Components, static component => component.Kind == DungeonComponentKind1458.EntranceHall);
        Assert.Contains(graph.Components, static component => component.Kind == DungeonComponentKind1458.Entrance);
    }

    [Theory]
    [InlineData(4200, 1200)]
    [InlineData(6400, 1800)]
    [InlineData(8400, 2400)]
    public void Canonical_dimensions_are_supported(int width, int height)
    {
        Assert.True(TerrainPass1458.IsCanonicalWorldSize(width, height));
    }

    [Theory]
    [InlineData(4199, 1200)]
    [InlineData(4200, 1199)]
    [InlineData(6401, 1800)]
    [InlineData(8400, 2401)]
    public void Noncanonical_dimensions_are_rejected_by_canonical_size_check(int width, int height)
    {
        Assert.False(TerrainPass1458.IsCanonicalWorldSize(width, height));
    }

    [Fact]
    public void Same_request_produces_deterministic_workspace_hash()
    {
        var request = new WorldGenerationRequest(VanillaId, "Deterministic", 123456789, 192, 128)
        {
            SeedText = "123456789"
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult first = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);
        RuntimeWorldCreationPipelineResult second = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());
        Assert.NotNull(first.Candidate);
        Assert.NotNull(second.Candidate);
        Assert.Equal(HashWorkspace(first.Candidate!), HashWorkspace(second.Candidate!));
    }

    [Fact]
    public void Different_seed_changes_compatible_world_hash()
    {
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        var firstRequest = new WorldGenerationRequest(VanillaId, "SeedA", 1, 192, 128) { SeedText = "1" };
        var secondRequest = new WorldGenerationRequest(VanillaId, "SeedB", 2, 192, 128) { SeedText = "2" };

        RuntimeWorldCreationPipelineResult first = pipeline.CreateCandidate(
            in firstRequest,
            cancellationToken: TestContext.Current.CancellationToken);
        RuntimeWorldCreationPipelineResult second = pipeline.CreateCandidate(
            in secondRequest,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());
        Assert.NotNull(first.Candidate);
        Assert.NotNull(second.Candidate);
        Assert.NotEqual(HashWorkspace(first.Candidate!), HashWorkspace(second.Candidate!));
    }

    [Fact]
    public void Compatible_world_sets_spawn_and_layers_inside_bounds()
    {
        var request = new WorldGenerationRequest(VanillaId, "Compat", 77, 320, 180) { SeedText = "77" };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.True(result.Candidate!.TryGetSpawn(out WorldGenerationPoint spawn));
        Assert.InRange(spawn.X, 0, request.WidthTiles - 1);
        Assert.InRange(spawn.Y, 0, request.HeightTiles - 1);
        Assert.True(result.Candidate.TryGetLayers(out WorldGenerationLayers layers));
        Assert.InRange(layers.WorldSurface, 0d, request.HeightTiles - 1d);
        Assert.InRange(layers.RockLayer, layers.WorldSurface, request.HeightTiles - 1d);
    }

    [Fact]
    public void Persistence_pipeline_enforces_budget_and_atomicity()
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        var huge = new WorldGenerationRequest(VanillaId, "Huge", 1, 8000, 5000)
        {
            SeedText = "1"
        };
        var persistence = new RuntimeWorldCreationPersistencePipeline(source, maxTileCount: 32_000_000);
        string hugePath = Path.Combine(Path.GetTempPath(), $"terraruntime-huge-{Guid.NewGuid():N}.wld");
        RuntimeWorldCreationPersistenceResult hugeResult = persistence.TryCreateAndPersist(
            huge,
            hugePath,
            Guid.NewGuid(),
            1,
            0,
            0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeWorldCreationPersistenceStatus.GenerationBudgetExceeded, hugeResult.Status);
        Assert.False(File.Exists(hugePath));

        var tiny = new WorldGenerationRequest(VanillaId, "Tiny", 1, 8, 8) { SeedText = "1" };
        string tinyPath = Path.Combine(Path.GetTempPath(), $"terraruntime-tiny-{Guid.NewGuid():N}.wld");
        RuntimeWorldCreationPersistenceResult tinyResult = persistence.TryCreateAndPersist(
            tiny,
            tinyPath,
            Guid.NewGuid(),
            1,
            0,
            0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(
            tinyResult.Status is RuntimeWorldCreationPersistenceStatus.Persisted or
                RuntimeWorldCreationPersistenceStatus.GenerationFailed or
                RuntimeWorldCreationPersistenceStatus.FinalizationFailed,
            $"Unexpected status {tinyResult.Status}");

        if (File.Exists(tinyPath))
            File.Delete(tinyPath);
    }

    [Fact]
    public void Executor_honors_cancellation_during_canonical_generation()
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        var request = new WorldGenerationRequest(VanillaId, "Cancelled", 1, 4200, 1200) { SeedText = "1" };
        var candidate = new Workspace(request.WidthTiles, request.HeightTiles);
        IWorldGenerationProvider? resolved = GetVanillaProvider(source, VanillaId);
        Assert.NotNull(resolved);
        var provider = Assert.IsType<SourceBackedFinal1458>(resolved);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        WorldGenerationExecutionResult execResult = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            candidate,
            cancellationToken: cts.Token);
        Assert.Equal(WorldGenerationExecutionStatus.Cancelled, execResult.Status);
    }

    [Theory]
    [InlineData(192, 128)]
    [InlineData(640, 240)]
    public void Noncanonical_world_uses_compatible_fallback_and_remains_valid(int w, int h)
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        var request = new WorldGenerationRequest(VanillaId, "Compat", 42, w, h) { SeedText = "42" };
        var pipeline = new RuntimeWorldCreationPipeline(source);
        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.Equal(w, result.Candidate!.WidthTiles);
        Assert.Equal(h, result.Candidate.HeightTiles);
    }

    private static IWorldGenerationProvider? GetVanillaProvider(
        ITerraRuntimeWorldGeneratorSource source,
        WorldGeneratorId id)
    {
        Assert.True(source.TryResolveWorldGenerator(id, out IWorldGenerationProvider? provider));
        return provider;
    }

    private static ulong HashWorkspace(Workspace workspace)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;

        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                Assert.True(workspace.TryGetTile(x, y, out WorldGenerationTile tile));
                Mix(tile.Type);
                Mix(tile.Wall);
                Mix(tile.FrameX);
                Mix(tile.FrameY);
                Mix((ushort)tile.Flags);
                Mix(tile.LiquidAmount);
                Mix(tile.TileColor);
                Mix(tile.WallColor);
                Mix(tile.Shape);
                Mix((byte)tile.LiquidKind);
            }
        }

        return hash;

        void Mix<T>(T value)
            where T : unmanaged
        {
            foreach (byte b in System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                         System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref value, 1)))
            {
                hash ^= b;
                hash *= prime;
            }
        }
    }

    private static void AssertSourceFramedTrees(Workspace workspace)
    {
        var topFrames = new HashSet<(short X, short Y)>();
        var branchFrames = new HashSet<(short X, short Y)>();
        var rootFrames = new HashSet<(short X, short Y)>();
        for (int variant = 0; variant < 3; variant++)
        {
            Add(topFrames, TreeFrameCatalog1458.Top(leafy: true, variant));
            Add(topFrames, TreeFrameCatalog1458.Top(leafy: false, variant));
            Add(branchFrames, TreeFrameCatalog1458.LeftBranch(leafy: true, variant));
            Add(branchFrames, TreeFrameCatalog1458.LeftBranch(leafy: false, variant));
            Add(branchFrames, TreeFrameCatalog1458.RightBranch(leafy: true, variant));
            Add(branchFrames, TreeFrameCatalog1458.RightBranch(leafy: false, variant));
            Add(rootFrames, TreeFrameCatalog1458.LeftRoot(variant));
            Add(rootFrames, TreeFrameCatalog1458.RightRoot(variant));
        }

        int treeCells = 0;
        int tops = 0;
        int branches = 0;
        int roots = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                if (!tile.IsActive || tile.TileType != VanillaTileIds.Trees)
                    continue;

                treeCells++;
                var frame = (tile.FrameX, tile.FrameY);
                tops += topFrames.Contains(frame) ? 1 : 0;
                branches += branchFrames.Contains(frame) ? 1 : 0;
                roots += rootFrames.Contains(frame) ? 1 : 0;
            }

        Assert.True(treeCells > 0, "Canonical generation must contain ordinary tree cells.");
        Assert.True(tops > 0, "Canonical trees must contain source-framed crowns.");
        Assert.True(branches > 0, "Canonical trees must contain source-framed branches.");
        Assert.True(roots > 0, "Canonical trees must contain source-framed roots.");

        static void Add(HashSet<(short X, short Y)> target, TreeFrame1458 frame) =>
            target.Add((frame.X, frame.Y));
    }

    private static void AssertSourceShapedTerrain(Workspace workspace)
    {
        var counts = new int[6];
        for (int x = 20; x < workspace.WidthTiles - 20; x++)
            for (int y = 20; y < workspace.HeightTiles - 20; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                if (tile.IsActive && tile.Shape < counts.Length)
                    counts[tile.Shape]++;
            }

        Assert.True(counts[(byte)TileShape1458.HalfBrick] > 0, "Smooth World must produce half-bricks.");
        Assert.True(counts[(byte)TileShape1458.SlopeDownRight] > 0, "Smooth World must produce slope 1.");
        Assert.True(counts[(byte)TileShape1458.SlopeDownLeft] > 0, "Smooth World must produce slope 2.");
        Assert.True(counts[(byte)TileShape1458.SlopeUpRight] > 0, "Smooth World must produce slope 3.");
        Assert.True(counts[(byte)TileShape1458.SlopeUpLeft] > 0, "Smooth World must produce slope 4.");
    }
}
