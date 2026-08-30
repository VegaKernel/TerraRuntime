using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedVanillaWorldGenerationUndergroundFinish1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_through_larva()
    {
        var provider = new SourceBackedVanillaWorldGenerationUndergroundFinish1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "UndergroundFinish",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(VanillaWorldGenerationProvider1458.GeneratorId, provider.Id);
        Assert.Equal(105, builder.Entries.Count);

        string[] expected =
        [
            "terraria:1.4.5.8/GemsInIceBiome",
            "terraria:1.4.5.8/RandomGems",
            "terraria:1.4.5.8/MossGrass",
            "terraria:1.4.5.8/MudsWallsInJungle",
            "terraria:1.4.5.8/Larva"
        ];

        int mushrooms = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationVegetation1458.MushroomsId);
        Assert.True(mushrooms >= 0);
        Assert.Equal(
            expected,
            builder.Entries.Skip(mushrooms + 1).Take(expected.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string id in expected)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, id).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Contains(SourceBackedVanillaWorldGenerationUndergroundFinish1458.LarvaId, secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_segment_matches_underground_finish_source_order()
    {
        string[] expected =
        [
            "Gems In Ice Biome",
            "Random Gems",
            "Moss Grass",
            "Muds Walls In Jungle",
            "Larva"
        ];

        string[] catalog = VanillaWorldGenerationPassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int mushrooms = Array.IndexOf(catalog, "Mushrooms");

        Assert.True(mushrooms >= 0);
        Assert.Equal(expected, catalog.Skip(mushrooms + 1).Take(expected.Length));
        Assert.Equal("Micro Biomes", catalog[mushrooms + 1 + expected.Length]);
    }

    [Fact]
    public void Larva_identity_is_frame_important_three_by_three_contract()
    {
        const int larva = 231;
        Assert.True(VanillaWorldFrameImportance326.IsFrameImportant(larva));

        var workspace = new RuntimeWorldGenerationWorkspace(16, 16);
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 3; dy++)
        {
            workspace.TileStore.SetInitialPopulationTile(5 + dx, 6 + dy, new WorldTile
            {
                Type = larva,
                Flags = WorldTileFlags.Active,
                FrameX = checked((short)(dx * 18)),
                FrameY = checked((short)(dy * 18)),
                Wall = 86
            });
        }

        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 3; dy++)
        {
            WorldTile tile = workspace.TileStore.Get(5 + dx, 6 + dy);
            Assert.True(tile.IsActive);
            Assert.Equal((ushort)larva, tile.Type);
            Assert.Equal((short)(dx * 18), tile.FrameX);
            Assert.Equal((short)(dy * 18), tile.FrameY);
        }
    }

    [Theory]
    [InlineData(192, 128)]
    [InlineData(4200, 1199)]
    [InlineData(4199, 1200)]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged(int width, int height)
    {
        var provider = new SourceBackedVanillaWorldGenerationUndergroundFinish1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: width,
            HeightTiles: height);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationUndergroundFinish1458.GemsInIceBiomeId);
    }

    [Fact]
    public void Special_seed_profile_keeps_compatibility_plan()
    {
        var provider = new SourceBackedVanillaWorldGenerationUndergroundFinish1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Drunk",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "05162020"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationUndergroundFinish1458.LarvaId);
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
}
