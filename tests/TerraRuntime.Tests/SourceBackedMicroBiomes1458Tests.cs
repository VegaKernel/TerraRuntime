using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedMicroBiomes1458Tests
{
    internal static void AssertTrackConnections(WorldTileStore tiles, int x, int y)
    {
        WorldTile tile = tiles.Get(x, y);
        // All36 original Minecart.Initialize frames, including pressure track21.
        // Values are row offsets; -2 is the no-connection sentinel after subtracting1.
        int[] left = [-2,0,-2,0,1,0,0,-1,-1,1,1,-2,-1,-2,-2,0,1,-2,-1,-2,-2,0,-2,0,-2,0,1,-2,-1,-2,0,0,-1,1,-1,1];
        int[] right = [-2,0,0,-2,0,1,-1,0,1,-1,-2,1,-2,-1,0,-2,-2,1,-2,-1,-2,0,0,-2,0,-2,-2,1,-2,-1,0,0,1,-1,1,-1];
        Assert.InRange(tile.FrameX, (short)0, (short)35);
        Assert.Equal(-1, tile.FrameY);
        foreach (int side in new[] { -1, 1 })
        {
            int dy = side == -1 ? left[tile.FrameX] : right[tile.FrameX];
            if (dy == -2) continue;
            WorldTile neighbor = tiles.Get(x + side, y + dy);
            Assert.True(neighbor.IsActive && neighbor.Type == 314, $"Disconnected track {x},{y}");
            Assert.InRange(neighbor.FrameX, (short)0, (short)35);
            Assert.Equal(-dy, side == -1 ? right[neighbor.FrameX] : left[neighbor.FrameX]);
        }
    }

    [Fact]
    public void Canonical_ordinary_world_registers_one_micro_biomes_pass_after_larva()
    {
        var provider = new SourceBackedMicroBiomes1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "MicroBiomes",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(106, builder.Entries.Count);
        int larva = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedUndergroundFinish1458.LarvaId);
        Assert.True(larva >= 0);
        Assert.Equal(SourceBackedMicroBiomes1458.MicroBiomesId, builder.Entries[larva + 1].Descriptor.Id);
        Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, builder.Entries[larva + 1].Descriptor.RngMode);
        Assert.Contains(SourceBackedUndergroundFinish1458.LarvaId,
            builder.Entries[larva + 1].Descriptor.RequiredAfter.ToArray());

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Contains(SourceBackedMicroBiomes1458.MicroBiomesId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_places_settle_liquids_again_immediately_after_micro_biomes()
    {
        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int index = Array.IndexOf(catalog, "Micro Biomes");

        Assert.True(index >= 0);
        Assert.Equal("Larva", catalog[index - 1]);
        Assert.Equal("Settle Liquids Again", catalog[index + 1]);
    }

    [Theory]
    [InlineData(192, 128)]
    [InlineData(4200, 1199)]
    [InlineData(4199, 1200)]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged(int width, int height)
    {
        var provider = new SourceBackedMicroBiomes1458();
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
            entry.Descriptor.Id == SourceBackedMicroBiomes1458.MicroBiomesId);
    }

    [Fact]
    public void Special_seed_profile_registers_early_prefix_without_micro_biomes()
    {
        var provider = new SourceBackedMicroBiomes1458();
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

        Assert.Equal(49, builder.Entries.Count);
        Assert.Contains(builder.Entries, static entry =>
            entry.Descriptor.Id.Value == "terraria:1.4.5.8/MudCavesToGrass");
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedMicroBiomes1458.MicroBiomesId);
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
