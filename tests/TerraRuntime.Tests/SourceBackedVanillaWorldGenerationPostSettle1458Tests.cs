using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedVanillaWorldGenerationPostSettle1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_from_remove_water_through_statues()
    {
        var provider = new SourceBackedVanillaWorldGenerationPostSettle1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "PostSettle",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(VanillaWorldGenerationProvider1458.GeneratorId, provider.Id);
        Assert.Equal(67, builder.Entries.Count);

        string[] expectedStage =
        [
            "terraria:1.4.5.8/RemoveWaterFromSand",
            "terraria:1.4.5.8/Oasis",
            "terraria:1.4.5.8/ShellPiles",
            "terraria:1.4.5.8/SmoothWorld",
            "terraria:1.4.5.8/Waterfalls",
            "terraria:1.4.5.8/Ice",
            "terraria:1.4.5.8/WallVariety",
            "terraria:1.4.5.8/LifeCrystals",
            "terraria:1.4.5.8/Statues"
        ];

        int settle = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationJungleStructures1458.SettleLiquidsId);
        Assert.True(settle >= 0);
        Assert.Equal(
            expectedStage,
            builder.Entries.Skip(settle + 1).Take(expectedStage.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string passId in expectedStage)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, secrets.Descriptor.RngMode);
        Assert.IsType<VanillaOrdinarySecretSeedCompatibilityBarrier1458>(secrets.Pass);
        Assert.Contains(SourceBackedVanillaWorldGenerationPostSettle1458.StatuesId, secrets.Descriptor.RequiredAfter.ToArray());

        Assert.IsType<VanillaSourceBackedBiomesCompatibilityBarrier1458>(Find(builder, "terraria:1.4.5.8/Biomes").Pass);
        Assert.IsType<VanillaSourceBackedCavesCompatibilityBarrier1458>(Find(builder, "terraria:1.4.5.8/Caves").Pass);
        Assert.IsType<VanillaSourceBackedOreCompatibilityBarrier1458>(Find(builder, "terraria:1.4.5.8/Ores").Pass);
    }

    [Fact]
    public void Pinned_catalog_segment_matches_source_order_from_first_settle_through_statues()
    {
        string[] expected =
        [
            "Remove Water From Sand",
            "Oasis",
            "Shell Piles",
            "Smooth World",
            "Waterfalls",
            "Ice",
            "Wall Variety",
            "Life Crystals",
            "Statues"
        ];

        string[] catalog = VanillaWorldGenerationPassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int settle = Array.IndexOf(catalog, "Settle Liquids");

        Assert.True(settle >= 0);
        Assert.Equal(expected, catalog.Skip(settle + 1).Take(expected.Length));
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedVanillaWorldGenerationPostSettle1458();
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
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationPostSettle1458.OasisId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationPostSettle1458.StatuesId);
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
