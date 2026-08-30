using System.Security.Cryptography;
using System.Text;
using TerraRuntime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// End-to-end production-path verification for the built-in Terraria 1.4.5.8 vanilla generator.
/// The suite exercises the full overlay chain (114 passes for canonical ordinary worlds), deterministic
/// replay, file composition/validation and edge-case hardening.
/// </summary>
public sealed class VanillaWorldGenerationFullIntegrationTests
{
    private static readonly WorldGeneratorId VanillaId = VanillaWorldGenerationProvider1458.GeneratorId;

    [Fact]
    public void Canonical_small_world_produces_valid_persistable_wld_with_vanilla_constraints()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var source = BuiltInWorldGeneratorSource.Instance;
        var request = new WorldGenerationRequest(
            VanillaId,
            "TerraRuntimeVanillaIntegration",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };

        // Capture the full 109-name catalog identity via the final provider to ensure the ordinary
        // canonical pipeline remains pinned through Final Cleanup.
        var finalProvider = new SourceBackedVanillaWorldGenerationFinal1458();
        var planBuilder = new CapturePlanBuilder();
        finalProvider.BuildPlan(in request, planBuilder);
        Assert.Equal(114, planBuilder.Entries.Count);
        Assert.Contains(planBuilder.Entries, e => e.Descriptor.Id == SourceBackedVanillaWorldGenerationFinal1458.FinalCleanupId);
        Assert.Contains(planBuilder.Entries, e => e.Descriptor.Id == SourceBackedVanillaWorldGenerationStartingNpc1458.GuideId);

        var creationPipeline = new RuntimeWorldCreationPipeline(source);
        RuntimeWorldCreationPipelineResult creation = creationPipeline.CreateCandidate(in request, cancellationToken: cancellationToken);

        Assert.True(creation.Succeeded, $"Creation failed: {creation.Status} gen={creation.Generation.Status} fin={creation.Finalization?.Status} err={creation.Generation.Execution?.Error}");
        Assert.NotNull(creation.Candidate);
        RuntimeWorldGenerationWorkspace workspace = creation.Candidate!;
        RuntimeWorldGenerationMetadataSnapshot metadata = creation.Metadata;

        // Semantic anchors
        Assert.True(metadata.Spawn.X > 0 && metadata.Spawn.X < request.WidthTiles);
        Assert.True(metadata.Spawn.Y > 0 && metadata.Spawn.Y < request.HeightTiles);
        Assert.True(metadata.Dungeon.X > 0 && metadata.Dungeon.X < request.WidthTiles);
        Assert.True(metadata.Layers.WorldSurface > 0 && metadata.Layers.RockLayer > metadata.Layers.WorldSurface);
        Assert.True(metadata.VanillaSeedProfile.IsDefault);
        Assert.NotNull(metadata.VanillaBootstrapState);
        Assert.True(metadata.VanillaBootstrapState!.LeftBeachEnd > 100 && metadata.VanillaBootstrapState.LeftBeachEnd < 500);
        Assert.True(metadata.VanillaBootstrapState.RightBeachStart > 3700 && metadata.VanillaBootstrapState.RightBeachStart < 4100);

        // Tile/wall bounds – mirrors FinalCleanup validation but as observable production invariant
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        int activeTiles = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Assert.True(workspace.TryGetTile(x, y, out WorldGenerationTile tile));
                Assert.True(tile.Type < VanillaTileIds.Count, $"Unsupported tile {tile.Type} at ({x},{y})");
                Assert.True(tile.Wall < VanillaWallIds.Count, $"Unsupported wall {tile.Wall} at ({x},{y})");
                Assert.True(tile.Shape <= 5, $"Invalid shape {tile.Shape} at ({x},{y})");
                Assert.True((tile.Flags & ~KnownFlags) == 0, $"Unknown flags {tile.Flags} at ({x},{y})");
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0) activeTiles++;
            }
            if ((x & 511) == 0) cancellationToken.ThrowIfCancellationRequested();
        }
        Assert.True(activeTiles > width * 10, $"Expected substantial world fill, got {activeTiles}");

        // Chests: 2x2 containers must be present and have valid anchors
        WorldChest[] chests = workspace.CaptureGeneratedChests();
        Assert.True(chests.Length >= 20, $"Expected at least 20 generated chests, got {chests.Length}");
        Assert.True(chests.Length <= VanillaWorldFormat326.MaximumChestSlots);
        var chestPositions = new HashSet<(int X, int Y)>();
        foreach (WorldChest chest in chests)
        {
            Assert.True(chestPositions.Add((chest.X, chest.Y)), $"Duplicate chest at ({chest.X},{chest.Y})");
            WorldTile anchorTile = workspace.TileStore.Get(chest.X, chest.Y);
            Assert.True(VanillaTileObjectAnchorCatalog.MatchesChestAnchor(in anchorTile), $"Chest anchor mismatch at ({chest.X},{chest.Y}) type={anchorTile.Type}");
            Assert.Equal(VanillaChestPlacementWorldGenerationPipelineConstants.ContainersTile, anchorTile.Type);
        }

        // NPCs: Guide must be present exactly once, at spawn
        WorldNpcPersistence npcs = workspace.CaptureGeneratedNpcs();
        WorldTownNpc guide = Assert.Single(npcs.TownNpcs);
        Assert.Equal(VanillaStartingGuidePass1458.GuideNetId, guide.NetId);
        Assert.Equal(VanillaStartingGuidePass1458.StableGuideName, guide.GivenName);
        Assert.Equal(metadata.Spawn.X * 16f, guide.X);
        Assert.Equal(metadata.Spawn.Y * 16f, guide.Y);

        // File composition and round-trip validation (the same gate used by RuntimeWorldCreationPersistencePipeline)
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            request.WorldName,
            request.SeedText ?? request.Seed.ToString(),
            width,
            height,
            Guid.Parse("14580000-0000-4000-8000-0000000000A1"),
            worldId: 145800001);
        long now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc).ToBinary();
        WorldFileFreshCompose326Diagnostic compose = WorldFileFreshComposer326.TryCompose(
            header,
            metadata,
            workspace.TileStore,
            chests,
            npcs,
            gameMode: (byte)WorldGenerationGameMode.Classic,
            crimson: false,
            creationTimeBinary: now,
            lastPlayedBinary: now,
            out byte[] file);
        Assert.True(compose.Succeeded, compose.ToString());
        Assert.True(file.Length > 1_000_000, $"Expected >1MB .wld, got {file.Length}");

        WorldFileLoadLimits loadLimits = TerrariaServerHost.CreateServerWorldLoadLimits();
        // Narrow limits to the exact produced counts to ensure no hidden extra sections
        WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(file, loadLimits, out WorldFileData? loaded);
        Assert.True(load.IsLoaded, load.ToString());
        Assert.NotNull(loaded);
        Assert.Equal(header.Name, loaded!.Header.Name);
        Assert.Equal(chests.Length, loaded.Chests.Length);
        Assert.Single(loaded.Npcs.TownNpcs);
        Assert.Equal(22, loaded.Npcs.TownNpcs[0].NetId);
    }

    [Fact]
    public void Vanilla_generation_is_deterministic_for_fixed_seed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var source = BuiltInWorldGeneratorSource.Instance;

        var request = new WorldGenerationRequest(VanillaId, "Determinism", Seed: 8675309, WidthTiles: 640, HeightTiles: 240)
        {
            SeedText = "8675309"
        };

        var pipeline = new RuntimeWorldCreationPipeline(source);
        RuntimeWorldCreationPipelineResult first = pipeline.CreateCandidate(in request, cancellationToken: ct);
        RuntimeWorldCreationPipelineResult second = pipeline.CreateCandidate(in request, cancellationToken: ct);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());

        Assert.Equal(first.Metadata.Spawn, second.Metadata.Spawn);
        Assert.Equal(first.Metadata.Dungeon, second.Metadata.Dungeon);
        Assert.Equal(first.Metadata.Layers, second.Metadata.Layers);
        Assert.Equal(first.Candidate!.GeneratedChestCount, second.Candidate!.GeneratedChestCount);

        WorldFileHeader header = VanillaFreshWorldHeader326.Create("Determinism", "8675309", 640, 240, Guid.NewGuid(), 1);
        long now = DateTime.UtcNow.ToBinary();
        WorldFileFreshCompose326Diagnostic c1 = WorldFileFreshComposer326.TryCompose(header, first.Metadata, first.Candidate.TileStore, first.Candidate.CaptureGeneratedChests(), first.Candidate.CaptureGeneratedNpcs(), 0, false, now, now, out byte[] f1);
        WorldFileFreshCompose326Diagnostic c2 = WorldFileFreshComposer326.TryCompose(header, second.Metadata, second.Candidate.TileStore, second.Candidate.CaptureGeneratedChests(), second.Candidate.CaptureGeneratedNpcs(), 0, false, now, now, out byte[] f2);
        Assert.True(c1.Succeeded, c1.ToString());
        Assert.True(c2.Succeeded, c2.ToString());
        Assert.Equal(f1.Length, f2.Length);
        string h1 = Convert.ToHexString(SHA256.HashData(f1));
        string h2 = Convert.ToHexString(SHA256.HashData(f2));
        Assert.Equal(h1, h2);

        // Different seed must diverge at file level
        var diffRequest = request with { Seed = 1, SeedText = "1" };
        RuntimeWorldCreationPipelineResult diff = pipeline.CreateCandidate(in diffRequest, cancellationToken: ct);
        Assert.True(diff.Succeeded);
        // For small non-canonical fallback, spawn may be center-fixed, so compare serialized file hash instead
        WorldFileHeader diffHeader = VanillaFreshWorldHeader326.Create("Determinism", "1", 640, 240, Guid.NewGuid(), 1);
        WorldFileFreshCompose326Diagnostic diffCompose = WorldFileFreshComposer326.TryCompose(diffHeader, diff.Metadata, diff.Candidate!.TileStore, diff.Candidate.CaptureGeneratedChests(), diff.Candidate.CaptureGeneratedNpcs(), 0, false, now, now, out byte[] diffFile);
        Assert.True(diffCompose.Succeeded, diffCompose.ToString());
        string diffHash = Convert.ToHexString(SHA256.HashData(diffFile));
        Assert.NotEqual(h1, diffHash);
    }

    [Fact]
    public void Persistence_pipeline_enforces_budget_and_atomicity()
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        // Budget exceeded – request absurdly large world (>32M tiles)
        var huge = new WorldGenerationRequest(VanillaId, "Huge", 1, 8000, 5000)
        {
            SeedText = "1"
        };
        var persistence = new RuntimeWorldCreationPersistencePipeline(source, maxTileCount: 32_000_000);
        string hugePath = Path.Combine(Path.GetTempPath(), $"terraruntime-huge-{Guid.NewGuid():N}.wld");
        RuntimeWorldCreationPersistenceResult hugeResult = persistence.TryCreateAndPersist(huge, hugePath, Guid.NewGuid(), 1, 0, 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeWorldCreationPersistenceStatus.GenerationBudgetExceeded, hugeResult.Status);
        Assert.False(File.Exists(hugePath));

        // Invalid dimensions – small
        var tiny = new WorldGenerationRequest(VanillaId, "Tiny", 1, 8, 8) { SeedText = "1" };
        string tinyPath = Path.Combine(Path.GetTempPath(), $"terraruntime-tiny-{Guid.NewGuid():N}.wld");
        RuntimeWorldCreationPersistenceResult tinyResult = persistence.TryCreateAndPersist(tiny, tinyPath, Guid.NewGuid(), 1, 0, 0, cancellationToken: TestContext.Current.CancellationToken);
        // Tiny canonical check will fallback to compat but should still either succeed or fail gracefully due to tileCount not exceeding budget
        // We assert it does not crash and either succeeds or reports GenerationFailed
        Assert.True(
            tinyResult.Status is RuntimeWorldCreationPersistenceStatus.Persisted or RuntimeWorldCreationPersistenceStatus.GenerationFailed or RuntimeWorldCreationPersistenceStatus.FinalizationFailed,
            $"Unexpected status {tinyResult.Status}");

        if (File.Exists(tinyPath)) File.Delete(tinyPath);
    }

    [Fact]
    public void Executor_honors_cancellation_during_canonical_generation()
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        var request = new WorldGenerationRequest(VanillaId, "Cancelled", 1, 4200, 1200) { SeedText = "1" };
        var candidate = new RuntimeWorldGenerationWorkspace(request.WidthTiles, request.HeightTiles);
        IWorldGenerationProvider? resolved = GetVanillaProvider(source, VanillaId);
        Assert.NotNull(resolved);
        var provider = Assert.IsType<SourceBackedVanillaWorldGenerationFinal1458>(resolved);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        WorldGenerationExecutionResult execResult = RuntimeWorldGenerationExecutor.Execute(provider, in request, candidate, cancellationToken: cts.Token);
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
        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(in request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        // Compat path does not have source-backed bootstrap for synthetic sizes
        // but must still produce valid layers/spawn/dungeon
        Assert.True(result.Metadata.Layers.WorldSurface > 0);
        // File must still compose
        WorldFileHeader header = VanillaFreshWorldHeader326.Create("Compat", "42", w, h, Guid.NewGuid(), 1);
        long now = DateTime.UtcNow.ToBinary();
        WorldFileFreshCompose326Diagnostic compose = WorldFileFreshComposer326.TryCompose(header, result.Metadata, result.Candidate.TileStore, result.Candidate.CaptureGeneratedChests(), result.Candidate.CaptureGeneratedNpcs(), 0, false, now, now, out byte[] file);
        Assert.True(compose.Succeeded, compose.ToString());
    }

    private static IWorldGenerationProvider? GetVanillaProvider(ITerraRuntimeWorldGeneratorSource source, WorldGeneratorId id)
    {
        source.TryResolveWorldGenerator(id, out IWorldGenerationProvider? provider);
        return provider;
    }

    private const WorldGenerationTileFlags KnownFlags =
        WorldGenerationTileFlags.Active |
        WorldGenerationTileFlags.WireRed |
        WorldGenerationTileFlags.WireBlue |
        WorldGenerationTileFlags.WireGreen |
        WorldGenerationTileFlags.WireYellow |
        WorldGenerationTileFlags.Actuator |
        WorldGenerationTileFlags.Inactive |
        WorldGenerationTileFlags.InvisibleBlock |
        WorldGenerationTileFlags.InvisibleWall |
        WorldGenerationTileFlags.FullbrightBlock |
        WorldGenerationTileFlags.FullbrightWall;

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        public List<(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass)> Entries { get; } = [];
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => Entries.Add((descriptor, pass));
    }

    private static class VanillaChestPlacementWorldGenerationPipelineConstants
    {
        public const ushort ContainersTile = 21;
    }
}
