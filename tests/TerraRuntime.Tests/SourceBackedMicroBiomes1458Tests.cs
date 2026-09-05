using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedMicroBiomes1458Tests
{
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

    [Fact]
    public void Pinned_micro_biome_tile_identities_match_format_contracts()
    {
        Assert.Equal((ushort)162, MicroBiomesPass1458.ThinIce);
        Assert.Equal((ushort)141, MicroBiomesPass1458.Explosives);
        Assert.Equal((ushort)215, MicroBiomesPass1458.Campfire);
        Assert.Equal((ushort)314, MicroBiomesPass1458.MinecartTrack);
        Assert.True(VanillaWorldFrameImportance326.IsFrameImportant(MicroBiomesPass1458.Campfire));
        Assert.True(VanillaWorldFrameImportance326.IsFrameImportant(MicroBiomesPass1458.LargePiles2));
    }

    [Fact]
    public void Track_placer_refuses_to_cut_through_frame_important_object()
    {
        var workspace = new Workspace(128, 256);
        workspace.TileStore.SetInitialPopulationTile(30, 30, new WorldTile
        {
            Type = MicroBiomesPass1458.Campfire,
            Flags = WorldTileFlags.Active,
            FrameX = 0,
            FrameY = 0
        });
        var grid = new MicroBiomesPass1458.RuntimeGrid(workspace);
        var protectedAreas = new MicroBiomesPass1458.ProtectedAreaIndex(workspace);

        bool placed = MicroBiomesPass1458.TryPlaceTrack(
            grid,
            protectedAreas,
            new ZeroSlopeRandom(),
            startX: 10,
            startY: 30,
            direction: 1,
            requestedLength: 80);

        Assert.False(placed);
        Assert.Equal(MicroBiomesPass1458.Campfire, workspace.TileStore.Get(30, 30).Type);
        Assert.NotEqual(MicroBiomesPass1458.MinecartTrack, workspace.TileStore.Get(10, 30).Type);
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
    public void Special_seed_profile_keeps_compatibility_plan()
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

        Assert.Equal(8, builder.Entries.Count);
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

    private sealed class ZeroSlopeRandom : IWorldGenerationVanillaRandom
    {
        public int Next() => 0;
        public int Next(int maxValue) => 0;
        public int Next(int minValue, int maxValue) => minValue <= 0 && maxValue > 0 ? 0 : minValue;
        public double NextDouble() => 0.5d;
        public void NextBytes(byte[] buffer) => Array.Clear(buffer);
    }
}
