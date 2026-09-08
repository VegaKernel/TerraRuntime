using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedMicroBiomes1458Tests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    public void Generated_tracks_connect_in_both_directions_with_no_phantom_switch(int direction, int slope)
    {
        var workspace = new Workspace(160, 400);
        var grid = new MicroBiomesPass1458.RuntimeGrid(workspace);
        var areas = new MicroBiomesPass1458.ProtectedAreaIndex(workspace);
        for (int x = 0; x < 160; x++)
        for (int y = 10; y < 180; y++)
            workspace.TileStore.SetInitialPopulationTile(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.True(MicroBiomesPass1458.TryPlaceTrack(grid, areas, new SlopeRandom(slope),
            direction == 1 ? 20 : 130, 90, direction, 80));
        int count = 0;
        for (int x = 1; x < 159; x++)
        for (int y = 1; y < 399; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            if (!tile.IsActive || tile.Type != 314) continue;
            count++;
            AssertTrackConnections(workspace.TileStore, x, y);
            for (int up = 1; up < 5; up++) Assert.False(workspace.TileStore.Get(x, y - up).IsActive);
        }
        Assert.Equal(80, count);
    }

    internal static void AssertTrackConnections(WorldTileStore tiles, int x, int y)
    {
        WorldTile tile = tiles.Get(x, y);
        // Independent literal Minecart.Initialize left/right connection table; -2 means no neighbor.
        (int Left, int Right)[] connections = [(-2, -2), (0, 0), (-2, 0), (0, -2),
            (1, 0), (0, 1), (0, -1), (-1, 0), (-1, 1), (1, -1), (1, -2), (-2, 1), (-1, -2), (-2, -1)];
        Assert.InRange(tile.FrameX, (short)1, (short)13);
        Assert.Equal(-1, tile.FrameY);
        var pair = connections[tile.FrameX];
        foreach (int side in new[] { -1, 1 })
        {
            int dy = side == -1 ? pair.Left : pair.Right;
            if (dy == -2) continue;
            WorldTile neighbor = tiles.Get(x + side, y + dy);
            Assert.True(neighbor.IsActive && neighbor.Type == 314, $"Disconnected track {x},{y}");
            Assert.InRange(neighbor.FrameX, (short)1, (short)13);
            var other = connections[neighbor.FrameX];
            Assert.Equal(-dy, side == -1 ? other.Right : other.Left);
        }
    }

    private sealed class SlopeRandom(int slope) : IWorldGenerationVanillaRandom
    {
        public int Next() => 0;
        public int Next(int maxValue) => 0;
        public int Next(int minValue, int maxValue) => minValue == -1 ? slope : minValue;
        public double NextDouble() => .5;
        public void NextBytes(byte[] buffer) => Array.Clear(buffer);
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

    [Theory]
    [InlineData(30)]
    [InlineData(24)]
    public void Track_placer_refuses_to_cut_through_frame_important_object(int objectY)
    {
        var workspace = new Workspace(128, 256);
        workspace.TileStore.SetInitialPopulationTile(30, objectY, new WorldTile
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
        Assert.Equal(MicroBiomesPass1458.Campfire, workspace.TileStore.Get(30, objectY).Type);
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
