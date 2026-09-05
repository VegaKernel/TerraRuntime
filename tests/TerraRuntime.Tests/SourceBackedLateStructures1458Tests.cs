using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedLateStructures1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_through_floating_island_houses()
    {
        var provider = new SourceBackedLateStructures1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "LateStructures",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(78, builder.Entries.Count);

        string[] expectedStage =
        [
            "terraria:1.4.5.8/SpiderCaves",
            "terraria:1.4.5.8/GemCaves",
            "terraria:1.4.5.8/Moss",
            "terraria:1.4.5.8/Temple",
            "terraria:1.4.5.8/CaveWalls",
            "terraria:1.4.5.8/JungleTrees",
            "terraria:1.4.5.8/FloatingIslandHouses"
        ];

        int waterChests = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedChestPlacement1458.WaterChestsId);
        Assert.True(waterChests >= 0);
        Assert.Equal(
            expectedStage,
            builder.Entries.Skip(waterChests + 1).Take(expectedStage.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string passId in expectedStage)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Contains(
            SourceBackedLateStructures1458.FloatingIslandHousesId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_segment_matches_late_structure_source_order()
    {
        string[] expected =
        [
            "Spider Caves",
            "Gem Caves",
            "Moss",
            "Temple",
            "Cave Walls",
            "Jungle Trees",
            "Floating Island Houses"
        ];

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int waterChests = Array.IndexOf(catalog, "Water Chests");

        Assert.True(waterChests >= 0);
        Assert.Equal(expected, catalog.Skip(waterChests + 1).Take(expected.Length));
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedLateStructures1458();
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
            entry.Descriptor.Id == SourceBackedLateStructures1458.SpiderCavesId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedLateStructures1458.FloatingIslandHousesId);
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
