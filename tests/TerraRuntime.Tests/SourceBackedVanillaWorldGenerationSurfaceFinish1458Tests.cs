using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedVanillaWorldGenerationSurfaceFinish1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_through_grass_wall()
    {
        var provider = new SourceBackedVanillaWorldGenerationSurfaceFinish1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "SurfaceFinish",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(VanillaWorldGenerationProvider1458.GeneratorId, provider.Id);
        Assert.Equal(88, builder.Entries.Count);

        string[] expected =
        [
            "terraria:1.4.5.8/QuickCleanup",
            "terraria:1.4.5.8/Pots",
            "terraria:1.4.5.8/Hellforge",
            "terraria:1.4.5.8/SpreadingGrass",
            "terraria:1.4.5.8/SurfaceOreAndStone",
            "terraria:1.4.5.8/PlaceFallenLog",
            "terraria:1.4.5.8/Traps",
            "terraria:1.4.5.8/Piles",
            "terraria:1.4.5.8/SpawnPoint",
            "terraria:1.4.5.8/GrassWall"
        ];

        int floatingHouses = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationLateStructures1458.FloatingIslandHousesId);
        Assert.True(floatingHouses >= 0);
        Assert.Equal(
            expected,
            builder.Entries.Skip(floatingHouses + 1).Take(expected.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string id in expected)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, id).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Contains(SourceBackedVanillaWorldGenerationSurfaceFinish1458.GrassWallId,
            secrets.Descriptor.RequiredAfter.ToArray());

        CaptureEntry metadata = Find(builder, "terraria:1.4.5.8/Metadata");
        Assert.IsType<VanillaSpawnPreservingMetadataPass1458>(metadata.Pass);
    }

    [Fact]
    public void Pinned_catalog_segment_matches_surface_finish_source_order()
    {
        string[] expected =
        [
            "Quick Cleanup",
            "Pots",
            "Hellforge",
            "Spreading Grass",
            "Surface Ore and Stone",
            "Place Fallen Log",
            "Traps",
            "Piles",
            "Spawn Point",
            "Grass Wall"
        ];

        string[] catalog = VanillaWorldGenerationPassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int floatingHouses = Array.IndexOf(catalog, "Floating Island Houses");
        Assert.True(floatingHouses >= 0);
        Assert.Equal(expected, catalog.Skip(floatingHouses + 1).Take(expected.Length));
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedVanillaWorldGenerationSurfaceFinish1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: 192,
            HeightTiles: 128);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationSurfaceFinish1458.QuickCleanupId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationSurfaceFinish1458.GrassWallId);
    }

    [Fact]
    public void Spawn_preserving_metadata_wrapper_restores_source_spawn_after_fallback()
    {
        var state = new VanillaSurfaceFinishWorldGenerationState1458
        {
            SpawnPoint = new WorldGenerationPoint(51, 37)
        };
        var fallback = new MetadataOverwritingPass();
        var pass = new VanillaSpawnPreservingMetadataPass1458(fallback, state);
        var workspace = new RuntimeWorldGenerationWorkspace(100, 80);
        var context = new TestContext(workspace);

        pass.Execute(context);

        Assert.True(workspace.TryGetSpawn(out WorldGenerationPoint spawn));
        Assert.Equal(new WorldGenerationPoint(51, 37), spawn);
    }

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, entry => entry.Descriptor.Id.Value == id);

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }

    private sealed class MetadataOverwritingPass : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            Assert.NotNull(context.Metadata);
            Assert.True(context.Metadata.TrySetSpawn(10, 10));
        }
    }

    private sealed class TestContext(RuntimeWorldGenerationWorkspace workspace) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => default;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace? Metadata => workspace;
        public IWorldGenerationRandom Random { get; } = new StubRandom();
        public IWorldGenerationVanillaRandom? VanillaRandom => null;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class StubRandom : IWorldGenerationRandom
    {
        public ulong NextUInt64() => 0;
        public uint NextUInt32() => 0;
        public int NextInt32(int exclusiveMax) => 0;
    }
}
