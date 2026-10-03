using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using System.Security.Cryptography;
using System.Text;

namespace TerraRuntime.Tests;

public sealed class SourceBackedUndergroundFinish1458Tests
{
    [Fact]
    public void Gems_in_ice_biome_matches_official_passlegacy_fixture()
    {
        const int width = 600;
        const int height = 800;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaLiquidLines(300, 600);
        int[] left = Enumerable.Repeat(100, height).ToArray();
        int[] right = Enumerable.Repeat(500, height).ToArray();
        workspace.SetVanillaSnowBounds(0, height, left, right);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));

        for (int x = 100; x < 500; x += 2)
        for (int y = 170; y < 600; y += 2)
        {
            var ice = new WorldTile { Type = 161, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in ice);
        }

        var random = new RandomAdapter(1458);
        new UndergroundFinishPass1458(UndergroundFinishStage1458.GemsInIceBiome, new UndergroundFinishState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        Assert.Equal(311, CountActiveType(workspace, 178));
        Assert.Equal(890412130, random.Next());
        Assert.Equal("9FAD9E80D4214EA33FFB1BF777AECFBF4334114EDB14F384A97C470DEEAF27FC", HashFixture(workspace));
    }

    [Fact]
    public void Random_gems_matches_official_passlegacy_fixture()
    {
        const int width = 600;
        const int height = 800;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaLiquidLines(300, 600);
        workspace.SetVanillaSnowBounds(0, height, Enumerable.Repeat(100, height).ToArray(), Enumerable.Repeat(500, height).ToArray());
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        for (int x = 0; x < width; x++)
        for (int y = 140; y < 500; y++)
        {
            var tile = new WorldTile { Wall = 216 };
            if (y % 5 == 0)
            {
                tile.Type = 1;
                tile.Flags = WorldTileFlags.Active;
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
            workspace.TileStore.Set(x, y, in tile);
        }

        var random = new RandomAdapter(1458);
        new UndergroundFinishPass1458(UndergroundFinishStage1458.RandomGems, new UndergroundFinishState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        Assert.Equal(3114, CountActiveType(workspace, 178));
        Assert.Equal(588323533, random.Next());
        Assert.Equal("2E2BEFD6313F5E14B7F09A9AF2C4077E90C22A6B1D334444A56412E39375908F", HashActiveGems(workspace));
    }

    [Fact]
    public void Muds_walls_in_jungle_matches_official_passlegacy_fixture()
    {
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaLiquidLines(300, 350);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));

        for (int x = 0; x < width; x++)
        for (int y = 0; y < 160; y++)
        {
            var tile = new WorldTile();
            if ((x + y) % 3 != 0)
                tile.Wall = (ushort)(((x + y) & 1) == 0 ? 2 : 59);
            workspace.TileStore.Set(x, y, in tile);
        }
        foreach ((int x, int y) in new[] { (150, 130), (450, 131) })
        {
            var tile = new WorldTile { Type = 60, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in tile);
        }

        var random = new RandomAdapter(1458);
        new UndergroundFinishPass1458(UndergroundFinishStage1458.MudsWallsInJungle, new UndergroundFinishState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        Assert.Equal(688508689, random.Next());
        Assert.Equal(31759, CountWalls(workspace, 15));
        Assert.Equal("3233D10B20501BE186BD87E05928E9D992177AA26A9630301AA56BB7F8BC39A6", HashWallCoordinates(workspace, 15));
    }

    [Fact]
    public void Canonical_ordinary_world_extends_source_order_through_larva()
    {
        var provider = new SourceBackedUndergroundFinish1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "UndergroundFinish",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
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
            entry.Descriptor.Id == SourceBackedVegetation1458.MushroomsId);
        Assert.True(mushrooms >= 0);
        Assert.Equal(
            expected,
            builder.Entries.Skip(mushrooms + 1).Take(expected.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string id in expected)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, id).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Contains(SourceBackedUndergroundFinish1458.LarvaId, secrets.Descriptor.RequiredAfter.ToArray());
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

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
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

        var workspace = new Workspace(16, 16);
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
        var provider = new SourceBackedUndergroundFinish1458();
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
            entry.Descriptor.Id == SourceBackedUndergroundFinish1458.GemsInIceBiomeId);
    }

    [Fact]
    public void Special_seed_profile_keeps_compatibility_plan()
    {
        var provider = new SourceBackedUndergroundFinish1458();
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
            entry.Descriptor.Id == SourceBackedUndergroundFinish1458.LarvaId);
    }

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, entry => entry.Descriptor.Id.Value == id);

    private static int CountActiveType(Workspace workspace, ushort type)
    {
        int count = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            if (tile.IsActive && tile.Type == type) count++;
        }
        return count;
    }

    private static int CountWalls(Workspace workspace, ushort wall)
    {
        int count = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
            if (workspace.TileStore.Get(x, y).Wall == wall) count++;
        return count;
    }

    private static string HashFixture(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private static string HashActiveGems(Workspace workspace)
    {
        var snapshot = new StringBuilder();
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            if (tile.IsActive && tile.Type == 178)
                snapshot.Append(x).Append(',').Append(y).Append('@').Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private static string HashWallCoordinates(Workspace workspace, ushort wall)
    {
        var snapshot = new StringBuilder();
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
            if (workspace.TileStore.Get(x, y).Wall == wall)
                snapshot.Append(x).Append(',').Append(y).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }

    private sealed class Context(
        WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
