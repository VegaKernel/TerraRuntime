using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedFinal1458Tests
{
    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");

    private static readonly WorldGenerationPassId[] FinalPassIds =
    [
        SourceBackedFinal1458.SettleLiquidsAgainId,
        SourceBackedFinal1458.CactusPalmTreesCoralId,
        SourceBackedFinal1458.TileCleanupId,
        SourceBackedFinal1458.LihzahrdAltarsId,
        SourceBackedFinal1458.WaterPlantsId,
        SourceBackedFinal1458.StalacId,
        SourceBackedFinal1458.RemoveBrokenTrapsId,
        SourceBackedFinal1458.FinalCleanupId
    ];

    [Fact]
    public void Canonical_ordinary_world_registers_final_eight_passes_in_pinned_order()
    {
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "FinalOverlay",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        int microBiomes = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedMicroBiomes1458.MicroBiomesId);
        Assert.True(microBiomes >= 0);

        for (int index = 0; index < FinalPassIds.Length; index++)
        {
            CaptureEntry entry = builder.Entries[microBiomes + index + 1];
            Assert.Equal(FinalPassIds[index], entry.Descriptor.Id);
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, entry.Descriptor.RngMode);

            WorldGenerationPassId expectedDependency = index == 0
                ? SourceBackedMicroBiomes1458.MicroBiomesId
                : FinalPassIds[index - 1];
            Assert.Contains(expectedDependency, entry.Descriptor.RequiredAfter.ToArray());
        }

        CaptureEntry secrets = Assert.Single(builder.Entries, static entry => entry.Descriptor.Id == SecretSeedsId);
        Assert.Contains(SourceBackedFinal1458.FinalCleanupId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_tail_matches_final_overlay_order()
    {
        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int microBiomes = Array.IndexOf(catalog, "Micro Biomes");
        string[] expected =
        [
            "Settle Liquids Again",
            "Cactus, Palm Trees, & Coral",
            "Tile Cleanup",
            "Lihzahrd Altars",
            "Water Plants",
            "Stalac",
            "Remove Broken Traps",
            "Final Cleanup"
        ];

        Assert.True(microBiomes >= 0);
        Assert.Equal(expected, catalog.Skip(microBiomes + 1).Take(expected.Length).ToArray());
    }

    [Theory]
    [InlineData(192, 128)]
    [InlineData(4200, 1199)]
    [InlineData(4199, 1200)]
    public void Noncanonical_world_does_not_inject_final_overlay(int width, int height)
    {
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: width,
            HeightTiles: height);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.DoesNotContain(builder.Entries, static entry => FinalPassIds.Contains(entry.Descriptor.Id));
    }

    [Fact]
    public void Special_seed_profile_does_not_inject_ordinary_final_overlay()
    {
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Drunk",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "05162020"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.DoesNotContain(builder.Entries, static entry => FinalPassIds.Contains(entry.Descriptor.Id));
    }

    [Fact]
    public void Pure_remix_keeps_later_overlays_on_the_compatibility_path()
    {
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Don't Dig Up",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "don't dig up"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(10, builder.Entries.Count);
        Assert.Contains(builder.Entries, static entry => entry.Descriptor.Id.Value == "terraria:1.4.5.8/TerrainLayers");
        Assert.Contains(builder.Entries, static entry => entry.Descriptor.Id.Value == "terraria:1.4.5.8/Dunes");
        Assert.DoesNotContain(builder.Entries, static entry => FinalPassIds.Contains(entry.Descriptor.Id));
        CaptureEntry terrain = Assert.Single(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedProvider1458.TerrainPassId);
        Assert.IsType<TerrainPass1458>(terrain.Pass);
    }

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }
}
