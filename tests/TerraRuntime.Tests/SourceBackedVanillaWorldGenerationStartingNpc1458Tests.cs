using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedVanillaWorldGenerationStartingNpc1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_through_guide()
    {
        var provider = new SourceBackedVanillaWorldGenerationStartingNpc1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Guide",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(VanillaWorldGenerationProvider1458.GeneratorId, provider.Id);
        Assert.Equal(89, builder.Entries.Count);
        int grassWall = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationSurfaceFinish1458.GrassWallId);
        Assert.True(grassWall >= 0);
        Assert.Equal(
            SourceBackedVanillaWorldGenerationStartingNpc1458.GuideId,
            builder.Entries[grassWall + 1].Descriptor.Id);
        Assert.Equal(
            WorldGenerationRngMode.VanillaSharedRng,
            builder.Entries[grassWall + 1].Descriptor.RngMode);
        Assert.IsType<VanillaStartingGuidePass1458>(builder.Entries[grassWall + 1].Pass);

        CaptureEntry secrets = Assert.Single(
            builder.Entries,
            static entry => entry.Descriptor.Id.Value == "terraria:1.4.5.8/SecretSeeds");
        Assert.Contains(
            SourceBackedVanillaWorldGenerationStartingNpc1458.GuideId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_places_guide_immediately_after_grass_wall()
    {
        string[] catalog = VanillaWorldGenerationPassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int grassWall = Array.IndexOf(catalog, "Grass Wall");

        Assert.True(grassWall >= 0);
        Assert.Equal("Guide", catalog[grassWall + 1]);
    }

    [Fact]
    public void Generated_town_npc_registry_detaches_snapshot_and_rejects_duplicate_identity()
    {
        var workspace = new RuntimeWorldGenerationWorkspace(128, 96);

        Assert.True(workspace.TryAddGeneratedTownNpc(
            VanillaStartingGuidePass1458.GuideNetId,
            VanillaStartingGuidePass1458.StableGuideName,
            64 * 16f,
            40 * 16f,
            homeless: true,
            homeTileX: 64,
            homeTileY: 40));
        Assert.False(workspace.TryAddGeneratedTownNpc(
            VanillaStartingGuidePass1458.GuideNetId,
            "Wyatt",
            65 * 16f,
            40 * 16f,
            homeless: true,
            homeTileX: 65,
            homeTileY: 40));

        WorldNpcPersistence snapshot = workspace.CaptureGeneratedNpcs();
        WorldTownNpc guide = Assert.Single(snapshot.TownNpcs);
        Assert.Equal(22, guide.NetId);
        Assert.Equal("Andrew", guide.GivenName);
        Assert.Equal(64 * 16f, guide.X);
        Assert.Equal(40 * 16f, guide.Y);
        Assert.True(guide.Homeless);
        Assert.Empty(snapshot.ShimmeredTownNpcIndices);
        Assert.Empty(snapshot.PersistentNpcs);
    }

    [Fact]
    public void Guide_pass_registers_town_npc_at_source_backed_spawn_without_consuming_rng()
    {
        var workspace = new RuntimeWorldGenerationWorkspace(128, 96);
        Assert.True(workspace.TrySetSpawn(64, 40));
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Guide",
            Seed: 1458,
            WidthTiles: 128,
            HeightTiles: 96);
        var context = new TestContext(request, workspace);

        VanillaStartingGuidePass1458.Instance.Execute(context);

        WorldTownNpc guide = Assert.Single(workspace.CaptureGeneratedNpcs().TownNpcs);
        Assert.Equal(22, guide.NetId);
        Assert.Equal(64 * 16f, guide.X);
        Assert.Equal(40 * 16f, guide.Y);
        Assert.Equal((64, 40), (guide.HomeTileX, guide.HomeTileY));
        Assert.True(guide.Homeless);
    }

    [Fact]
    public void Fresh_composer_roundtrips_generated_guide_through_npc_section()
    {
        const int width = 128;
        const int height = 96;
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "GuidePersistence",
            "1458",
            width,
            height,
            Guid.Parse("c529774e-b1c7-41e5-ad9e-25ef493ba04d"),
            worldId: 1458);
        var tiles = new WorldTileStore(header.Dimensions);
        for (int x = 0; x < width; x++)
        for (int y = 48; y < height; y++)
        {
            tiles.SetInitialPopulationTile(x, y, new WorldTile
            {
                Type = y < 64 ? (ushort)0 : (ushort)1,
                Flags = WorldTileFlags.Active
            });
        }

        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(64, 40),
            new WorldGenerationPoint(12, 55),
            new WorldGenerationLayers(48d, 64d));
        var npcs = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(22, "Andrew", 64 * 16f, 40 * 16f, true, 64, 40, null, false)],
            []);

        WorldFileFreshCompose326Diagnostic compose = WorldFileFreshComposer326.TryCompose(
            header,
            generation,
            tiles,
            ReadOnlySpan<WorldChest>.Empty,
            npcs,
            gameMode: 0,
            crimson: false,
            creationTimeBinary: 0,
            lastPlayedBinary: 0,
            out byte[] file);

        Assert.True(compose.Succeeded, compose.ToString());
        WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
            file,
            CreateLimits(width * height),
            out WorldFileData? world);
        Assert.True(load.IsLoaded, load.ToString());
        Assert.NotNull(world);
        WorldTownNpc guide = Assert.Single(world.Npcs.TownNpcs);
        Assert.Equal(22, guide.NetId);
        Assert.Equal("Andrew", guide.GivenName);
        Assert.Equal(64 * 16f, guide.X);
        Assert.Equal(40 * 16f, guide.Y);
        Assert.True(guide.Homeless);
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedVanillaWorldGenerationStartingNpc1458();
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
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationStartingNpc1458.GuideId);
    }

    private static WorldFileLoadLimits CreateLimits(long tileCount) =>
        new(
            MaxTileCount: tileCount,
            MaxItemsPerChest: 0,
            MaxTotalChestItems: 0,
            MaxTextBytesPerSign: 0,
            MaxTotalSignTextBytes: 0,
            Npcs: new WorldFileNpcDecodeOptions(
                MaxShimmeredTownNpcIndices: 0,
                MaxShimmerIndexExclusive: 0,
                MaxTownNpcs: 1,
                MaxPersistentNpcs: 0,
                MaxNameBytesPerTownNpc: 64,
                MaxTotalNameBytes: 64),
            MaxTileEntities: 0,
            MaxPressurePlates: 0,
            MaxTownRooms: 0,
            Bestiary: new WorldFileBestiaryLimits(0, 0, 0, 0, 0),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(4096, 12288, 0, 0, 0, 0));

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }

    private sealed class TestContext : IWorldGenerationContext
    {
        public TestContext(WorldGenerationRequest request, RuntimeWorldGenerationWorkspace workspace)
        {
            Request = request;
            Workspace = workspace;
            Metadata = workspace;
        }

        public WorldGenerationRequest Request { get; }
        public IWorldGenerationWorkspace Workspace { get; }
        public IWorldGenerationMetadataWorkspace? Metadata { get; }
        public IWorldGenerationRandom Random { get; } = new StubRandom();
        public IWorldGenerationVanillaRandom? VanillaRandom => null;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null)
        {
        }
    }

    private sealed class StubRandom : IWorldGenerationRandom
    {
        public ulong NextUInt64() => 0;
        public uint NextUInt32() => 0;
        public int NextInt32(int exclusiveMax) => 0;
    }
}
