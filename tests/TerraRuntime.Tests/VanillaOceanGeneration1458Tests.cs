using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaOceanGeneration1458Tests
{
    [Theory]
    [InlineData(false, 1, 0.2d)]
    [InlineData(false, 2, 0.2d)]
    [InlineData(false, 3, 0.15d)]
    [InlineData(false, 199, 0.001d)]
    [InlineData(false, 200, 0.01d)]
    [InlineData(false, 254, 0.01d)]
    [InlineData(false, 255, 0d)]
    [InlineData(true, 1, 0.001d)]
    [InlineData(true, 149, 0.038d)]
    [InlineData(true, 244, 0.43d)]
    [InlineData(true, 254, 0.6d)]
    [InlineData(true, 255, 0d)]
    public void Depth_profile_matches_pinned_TuneOceanDepth_bands(
        bool floridaStyle,
        int inlandColumn,
        double expectedScale)
    {
        Assert.Equal(
            expectedScale,
            VanillaOceanGenerationCatalog1458.GetDepthIncrementScale(inlandColumn, floridaStyle));
    }

    [Fact]
    public void Canonical_provider_leaves_final_cleanup_unwrapped()
    {
        var provider = new SourceBackedVanillaWorldGenerationFinal1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "OceanPlan",
            1458,
            4200,
            1200)
        {
            SeedText = "1458",
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        CaptureEntry finalCleanup = Assert.Single(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationFinal1458.FinalCleanupId);
        Assert.DoesNotContain("Ocean", finalCleanup.Pass.GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Integrity_gate_accepts_connected_rising_sand_basin()
    {
        WorldTileStore store = CreateSyntheticOcean(dryGapStart: -1, dryGapWidth: 0);

        VanillaOceanIntegrityResult1458 result = VanillaOceanIntegrity1458.Validate(
            store,
            beachBoundary: 250,
            left: true,
            worldSurface: 100d);

        Assert.True(result.IsValid, result.Detail);
    }

    [Fact]
    public void Integrity_gate_rejects_wide_break_in_edge_connected_water()
    {
        WorldTileStore store = CreateSyntheticOcean(dryGapStart: 80, dryGapWidth: 12);

        VanillaOceanIntegrityResult1458 result = VanillaOceanIntegrity1458.Validate(
            store,
            beachBoundary: 250,
            left: true,
            worldSurface: 100d);

        Assert.False(result.IsValid);
        Assert.Contains("dry break", result.Detail, StringComparison.Ordinal);
    }

    private static WorldTileStore CreateSyntheticOcean(int dryGapStart, int dryGapWidth)
    {
        var store = new WorldTileStore(new WorldDimensions(420, 500));
        for (int x = 0; x < 220; x++)
        {
            if (x >= dryGapStart && x < dryGapStart + dryGapWidth)
                continue;

            int floor = 180 - x / 4;
            for (int y = 100; y < floor; y++)
            {
                ref WorldTile water = ref store.Tiles[store.GetUncheckedIndex(x, y)];
                water.LiquidAmount = byte.MaxValue;
                water.LiquidKind = WorldLiquidKind.Water;
            }

            ref WorldTile sand = ref store.Tiles[store.GetUncheckedIndex(x, floor)];
            sand.Type = VanillaOceanGenerationCatalog1458.SandTileType;
            sand.Flags = WorldTileFlags.Active;
        }

        return store;
    }

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
