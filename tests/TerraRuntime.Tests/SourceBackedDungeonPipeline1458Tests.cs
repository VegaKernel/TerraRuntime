using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedDungeonPipeline1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_from_dual_dungeons_through_pyramids()
    {
        var provider = new SourceBackedDungeonPipeline1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "DungeonStage",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(49, builder.Entries.Count);
        string[] expected =
        [
            "terraria:1.4.5.8/Reset",
            "terraria:1.4.5.8/Terrain",
            "terraria:1.4.5.8/TerrainLayers",
            "terraria:1.4.5.8/Dunes",
            "terraria:1.4.5.8/OceanSand",
            "terraria:1.4.5.8/SandPatches",
            "terraria:1.4.5.8/Tunnels",
            "terraria:1.4.5.8/MountCaves",
            "terraria:1.4.5.8/DirtWallBackgrounds",
            "terraria:1.4.5.8/RocksInDirt",
            "terraria:1.4.5.8/DirtInRocks",
            "terraria:1.4.5.8/Clay",
            "terraria:1.4.5.8/SmallHoles",
            "terraria:1.4.5.8/DirtLayerCaves",
            "terraria:1.4.5.8/RockLayerCaves",
            "terraria:1.4.5.8/SurfaceCaves",
            "terraria:1.4.5.8/WavyCaves",
            "terraria:1.4.5.8/GenerateIceBiome",
            "terraria:1.4.5.8/Grass",
            "terraria:1.4.5.8/Jungle",
            "terraria:1.4.5.8/MudCavesToGrass",
            "terraria:1.4.5.8/FullDesert",
            "terraria:1.4.5.8/MushroomPatches",
            "terraria:1.4.5.8/Marble",
            "terraria:1.4.5.8/Granite",
            "terraria:1.4.5.8/FloatingIslands",
            "terraria:1.4.5.8/DirtToMud",
            "terraria:1.4.5.8/Silt",
            "terraria:1.4.5.8/Shinies",
            "terraria:1.4.5.8/Webs",
            "terraria:1.4.5.8/Underworld",
            "terraria:1.4.5.8/Corruption",
            "terraria:1.4.5.8/Lakes",
            "terraria:1.4.5.8/Slush",
            "terraria:1.4.5.8/Biomes",
            "terraria:1.4.5.8/Caves",
            "terraria:1.4.5.8/Ores",
            "terraria:1.4.5.8/DualDungeonsDitherSnake",
            "terraria:1.4.5.8/Dungeon",
            "terraria:1.4.5.8/MountainCaves",
            "terraria:1.4.5.8/Beaches",
            "terraria:1.4.5.8/Gems",
            "terraria:1.4.5.8/GravitatingSand",
            "terraria:1.4.5.8/CreateOceanCaves",
            "terraria:1.4.5.8/Shimmer",
            "terraria:1.4.5.8/CleanUpDirt",
            "terraria:1.4.5.8/Pyramids",
            "terraria:1.4.5.8/SecretSeeds",
            "terraria:1.4.5.8/Metadata"
        ];

        Assert.Equal(expected, builder.Entries.Select(static e => e.Descriptor.Id.Value));

        string[] stagePasses =
        [
            "terraria:1.4.5.8/DualDungeonsDitherSnake",
            "terraria:1.4.5.8/Dungeon",
            "terraria:1.4.5.8/MountainCaves",
            "terraria:1.4.5.8/Beaches",
            "terraria:1.4.5.8/Gems",
            "terraria:1.4.5.8/GravitatingSand",
            "terraria:1.4.5.8/CreateOceanCaves",
            "terraria:1.4.5.8/Shimmer",
            "terraria:1.4.5.8/CleanUpDirt",
            "terraria:1.4.5.8/Pyramids"
        ];

        foreach (string passId in stagePasses)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry caves = Find(builder, "terraria:1.4.5.8/Caves");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, caves.Descriptor.RngMode);
        Assert.IsType<SourceBackedCavesCompatibilityBarrier1458>(caves.Pass);

        CaptureEntry ores = Find(builder, "terraria:1.4.5.8/Ores");
        Assert.IsType<SourceBackedOreCompatibilityBarrier1458>(ores.Pass);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, secrets.Descriptor.RngMode);
        Assert.IsType<OrdinarySecretSeedCompatibilityBarrier1458>(secrets.Pass);
        Assert.Contains(
            SourceBackedDungeonPipeline1458.PyramidsId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_segment_matches_the_source_order_through_pyramids()
    {
        string[] expected =
        [
            "Dual Dungeons Dither Snake",
            "Dungeon",
            "Mountain Caves",
            "Beaches",
            "Gems",
            "Gravitating Sand",
            "Create Ocean Caves",
            "Shimmer",
            "Clean Up Dirt",
            "Pyramids"
        ];

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int slush = Array.IndexOf(catalog, "Slush");

        Assert.True(slush >= 0);
        Assert.Equal(expected, catalog.Skip(slush + 1).Take(expected.Length));
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedDungeonPipeline1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: 192,
            HeightTiles: 128);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedDungeonPipeline1458.PyramidsId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedDungeonPipeline1458.ShimmerId);
    }

    [Fact]
    public void Ordinary_pyramid_candidate_filter_matches_source_edges_dungeon_side_and_spacing()
    {
        VanillaPyramidCandidate1458[] candidates =
        [
            new(300, 200),
            new(1200, 200),
            new(1300, 200),
            new(2000, 200),
            new(3900, 200)
        ];

        Assert.False(DungeonPass1458.IsOrdinaryPyramidCandidatePositionEligible(
            candidates, 0, worldWidth: 4200, dungeonSide: -1, dungeonGenerationX: 400));
        Assert.True(DungeonPass1458.IsOrdinaryPyramidCandidatePositionEligible(
            candidates, 1, worldWidth: 4200, dungeonSide: -1, dungeonGenerationX: 400));
        Assert.False(DungeonPass1458.IsOrdinaryPyramidCandidatePositionEligible(
            candidates, 2, worldWidth: 4200, dungeonSide: -1, dungeonGenerationX: 400));
        Assert.True(DungeonPass1458.IsOrdinaryPyramidCandidatePositionEligible(
            candidates, 3, worldWidth: 4200, dungeonSide: -1, dungeonGenerationX: 400));
        Assert.False(DungeonPass1458.IsOrdinaryPyramidCandidatePositionEligible(
            candidates, 4, worldWidth: 4200, dungeonSide: -1, dungeonGenerationX: 400));

        VanillaPyramidCandidate1458[] rightDungeonCandidates = [new(3000, 200), new(3300, 200)];
        Assert.True(DungeonPass1458.IsOrdinaryPyramidCandidatePositionEligible(
            rightDungeonCandidates, 0, worldWidth: 4200, dungeonSide: 1, dungeonGenerationX: 3800));
        Assert.False(DungeonPass1458.IsOrdinaryPyramidCandidatePositionEligible(
            rightDungeonCandidates, 1, worldWidth: 4200, dungeonSide: 1, dungeonGenerationX: 3800));
    }

    [Theory]
    [InlineData(4200, -1, 210, 1888)]
    [InlineData(4200, 1, 2310, 3989)]
    [InlineData(6400, -1, 320, 2878)]
    [InlineData(6400, 1, 3520, 6079)]
    [InlineData(8400, -1, 420, 3778)]
    [InlineData(8400, 1, 4620, 7979)]
    public void Dungeon_potential_bounds_match_pinned_source_float_casts(
        int worldWidth,
        int dungeonSide,
        int expectedMinimum,
        int expectedMaximum)
    {
        (int minimum, int maximum) = DungeonGraphGenerator1458.ResolveHorizontalBounds(
            worldWidth,
            dungeonSide);

        Assert.Equal(expectedMinimum, minimum);
        Assert.Equal(expectedMaximum, maximum);
    }

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, entry => entry.Descriptor.Id.Value == id);

    private readonly record struct CaptureEntry(
        WorldGenerationPassDescriptor Descriptor,
        IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }
}
