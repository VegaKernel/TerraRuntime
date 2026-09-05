using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedJungleStructures1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_from_dirt_rock_walls_through_first_liquid_settle()
    {
        var provider = new SourceBackedJungleStructures1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "JungleStructures",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(58, builder.Entries.Count);

        string[] expectedStage =
        [
            "terraria:1.4.5.8/DirtRockWallRunner",
            "terraria:1.4.5.8/LivingTrees",
            "terraria:1.4.5.8/WoodTreeWalls",
            "terraria:1.4.5.8/Altars",
            "terraria:1.4.5.8/WetJungle",
            "terraria:1.4.5.8/JungleTemple",
            "terraria:1.4.5.8/Hives",
            "terraria:1.4.5.8/JungleChests",
            "terraria:1.4.5.8/SettleLiquids"
        ];

        int pyramids = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedDungeonPipeline1458.PyramidsId);
        Assert.True(pyramids >= 0);
        Assert.Equal(
            expectedStage,
            builder.Entries.Skip(pyramids + 1).Take(expectedStage.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string passId in expectedStage)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry biomes = Find(builder, "terraria:1.4.5.8/Biomes");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, biomes.Descriptor.RngMode);
        Assert.IsType<SourceBackedBiomesCompatibilityBarrier1458>(biomes.Pass);

        CaptureEntry caves = Find(builder, "terraria:1.4.5.8/Caves");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, caves.Descriptor.RngMode);
        Assert.IsType<SourceBackedCavesCompatibilityBarrier1458>(caves.Pass);

        CaptureEntry ores = Find(builder, "terraria:1.4.5.8/Ores");
        Assert.IsType<SourceBackedOreCompatibilityBarrier1458>(ores.Pass);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, secrets.Descriptor.RngMode);
        Assert.IsType<OrdinarySecretSeedCompatibilityBarrier1458>(secrets.Pass);
        Assert.Contains(
            SourceBackedJungleStructures1458.SettleLiquidsId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_segment_matches_source_order_from_pyramids_through_first_liquid_settle()
    {
        string[] expected =
        [
            "Dirt Rock Wall Runner",
            "Living Trees",
            "Wood Tree Walls",
            "Altars",
            "Wet Jungle",
            "Jungle Temple",
            "Hives",
            "Jungle Chests",
            "Settle Liquids"
        ];

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int pyramids = Array.IndexOf(catalog, "Pyramids");

        Assert.True(pyramids >= 0);
        Assert.Equal(expected, catalog.Skip(pyramids + 1).Take(expected.Length));
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedJungleStructures1458();
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
            entry.Descriptor.Id == SourceBackedJungleStructures1458.JungleTempleId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedJungleStructures1458.SettleLiquidsId);
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
