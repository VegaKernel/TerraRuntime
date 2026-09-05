using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedVegetation1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_through_mushrooms()
    {
        var provider = new SourceBackedVegetation1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Vegetation",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(100, builder.Entries.Count);

        string[] expectedStage =
        [
            "terraria:1.4.5.8/Sunflowers",
            "terraria:1.4.5.8/PlantingTrees",
            "terraria:1.4.5.8/Herbs",
            "terraria:1.4.5.8/DyePlants",
            "terraria:1.4.5.8/WebsAndHoney",
            "terraria:1.4.5.8/Weeds",
            "terraria:1.4.5.8/GlowingMushroomsAndJunglePlants",
            "terraria:1.4.5.8/JunglePlants",
            "terraria:1.4.5.8/Vines",
            "terraria:1.4.5.8/Flowers",
            "terraria:1.4.5.8/Mushrooms"
        ];

        int guide = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedStartingNpc1458.GuideId);
        Assert.True(guide >= 0);
        Assert.Equal(
            expectedStage,
            builder.Entries.Skip(guide + 1).Take(expectedStage.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string passId in expectedStage)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Contains(SourceBackedVegetation1458.MushroomsId, secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_segment_matches_vegetation_source_order()
    {
        string[] expected =
        [
            "Sunflowers",
            "Planting Trees",
            "Herbs",
            "Dye Plants",
            "Webs And Honey",
            "Weeds",
            "Glowing Mushrooms and Jungle Plants",
            "Jungle Plants",
            "Vines",
            "Flowers",
            "Mushrooms"
        ];

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int guide = Array.IndexOf(catalog, "Guide");

        Assert.True(guide >= 0);
        Assert.Equal(expected, catalog.Skip(guide + 1).Take(expected.Length));
        Assert.Equal("Gems In Ice Biome", catalog[guide + 1 + expected.Length]);
    }

    [Theory]
    [InlineData(192, 128)]
    [InlineData(4200, 1199)]
    [InlineData(4199, 1200)]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged(int width, int height)
    {
        var provider = new SourceBackedVegetation1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: width,
            HeightTiles: height);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVegetation1458.SunflowersId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVegetation1458.MushroomsId);
    }

    [Fact]
    public void Special_seed_profile_does_not_claim_ordinary_vegetation_parity()
    {
        var provider = new SourceBackedVegetation1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Celebration",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "celebrationmk10"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVegetation1458.SunflowersId);
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
