using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedChestPlacement1458Tests
{
    [Theory]
    [InlineData(1458, 600, 500, "25DE7D0706CA668C3A3EB6B6E5ACD436D3B5FBE577AD85EABA1661F0D8BF15E4", 200, 201, 997576893)]
    [InlineData(1458, 4200, 1200, "28EA53B3B6CDFF3B07C79502DE8F5B100C9B11349B9F57FEE176EC660A30DDC9", 1000, 601, 997576893)]
    [InlineData(1, 600, 500, "6F888379F2E68FBE936B218097F9C69B536E03ACD85352264A80F915ED9369A5", 199, 201, 259488708)]
    [InlineData(1, 4200, 1200, "778EAAA6E3BC6FEE05BE847F60C09563FC80041C2F5BCD81A2D6AE605813FAF2", 999, 601, 259488708)]
    [InlineData(42, 600, 500, "6F888379F2E68FBE936B218097F9C69B536E03ACD85352264A80F915ED9369A5", 199, 201, 1107915559)]
    [InlineData(42, 4200, 1200, "778EAAA6E3BC6FEE05BE847F60C09563FC80041C2F5BCD81A2D6AE605813FAF2", 999, 601, 1107915559)]
    [InlineData(8675309, 600, 500, "25DE7D0706CA668C3A3EB6B6E5ACD436D3B5FBE577AD85EABA1661F0D8BF15E4", 200, 201, 1335694251)]
    [InlineData(8675309, 4200, 1200, "28EA53B3B6CDFF3B07C79502DE8F5B100C9B11349B9F57FEE176EC660A30DDC9", 1000, 601, 1335694251)]
    public void Jungle_chest_placement_consumes_published_shrine_anchors_and_matches_official_pass(
        int seed, int width, int height, string digest, int firstX, int chestY, int nextRandom)
    {
        // Registered TerrariaServer 1.4.5.8 ChestsInJungleShrines, seed1458:
        // first offer succeeds, second must clear a platform and retry, third
        // has no floor and fails after its cleanup/retry.
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(height == 1200 ? 300 : 140, height == 1200 ? 500 : 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        int[] anchors = height == 1200 ? [1000, 2000, 3000] : [200, 300, 400];
        int row = height == 1200 ? 600 : 200;
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int index = 0; index < 2; index++)
        for (int x = anchors[index] - 2; x <= anchors[index] + 2; x++)
            workspace.TileStore.Set(x, row + 3, in stone);
        var platform = new WorldTile { Type = 19, Flags = WorldTileFlags.Active };
        foreach (int x in new[] { anchors[1], anchors[2] })
            workspace.TileStore.Set(x, row + 1, in platform);
        workspace.SetVanillaJungleShrineChestAnchors(
            anchors.Select(x => new WorldGenerationPoint(x, row)).ToArray());

        var random = new RandomAdapter(seed);
        new ChestPlacementPass1458(ChestPlacementStage1458.JungleChestsPlacement, new ChestPlacementState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Shrines", checked((ulong)seed), width, height),
                workspace, random));

        WorldChest[] chests = workspace.CaptureGeneratedChests();
        Assert.Equal(2, chests.Length);
        int secondX = anchors[1] - 1;
        Assert.Equal((firstX, chestY), (chests[0].X, chests[0].Y));
        Assert.Equal((secondX, chestY), (chests[1].X, chests[1].Y));
        (string firstItems, string secondItems) = seed switch
        {
            1458 => ("211x1p70 3360x1 3361x1 21x10 279x40 296x2 8x24 73x1 2204x1",
                "212x1p76 5525x1 43x1 41x41 295x2 2351x2 2350x3 282x18 2204x1"),
            1 => ("211x1p74 3360x1 3361x1 167x1 19x5 295x2 2351x2 2350x2 282x17",
                "212x1 43x1 21x8 41x42 295x1 2350x3 73x1"),
            42 => ("211x1p65 3360x1 3361x1 167x1 19x7 41x31 2329x1 2350x2",
                "212x1p65 3360x1 3361x1 167x1 188x3 73x1"),
            8675309 => ("211x1p71 3360x1 3361x1 4426x1 21x10 188x5 303x2",
                "212x1p65 3360x1 3361x1 43x1 51x30 41x34 305x1 8x20 73x2"),
            _ => throw new ArgumentOutOfRangeException(nameof(seed))
        };
        Assert.Equal($"[{firstX},{chestY}]{firstItems} [{secondX},{chestY}]{secondItems}",
            ChestInventory(chests));
        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(nextRandom, random.Next());
    }

    [Fact]
    public void Canonical_ordinary_world_extends_source_order_through_water_chests()
    {
        var provider = new SourceBackedChestPlacement1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "ChestPlacement",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(71, builder.Entries.Count);

        string[] expectedStage =
        [
            "terraria:1.4.5.8/BuriedChests",
            "terraria:1.4.5.8/SurfaceChests",
            "terraria:1.4.5.8/JungleChestsPlacement",
            "terraria:1.4.5.8/WaterChests"
        ];

        int statues = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedPostSettle1458.StatuesId);
        Assert.True(statues >= 0);
        Assert.Equal(
            expectedStage,
            builder.Entries.Skip(statues + 1).Take(expectedStage.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string passId in expectedStage)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, secrets.Descriptor.RngMode);
        Assert.IsType<OrdinarySecretSeedCompatibilityBarrier1458>(secrets.Pass);
        Assert.Contains(SourceBackedChestPlacement1458.WaterChestsId, secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_segment_matches_chest_placement_source_order()
    {
        string[] expected =
        [
            "Buried Chests",
            "Surface Chests",
            "Jungle Chests Placement",
            "Water Chests"
        ];

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int statues = Array.IndexOf(catalog, "Statues");

        Assert.True(statues >= 0);
        Assert.Equal(expected, catalog.Skip(statues + 1).Take(expected.Length));
    }

    [Fact]
    public void Generated_chest_registry_assigns_dense_slots_and_rejects_duplicate_anchor()
    {
        var workspace = new Workspace(32, 24);
        PlaceChestTiles(workspace.TileStore, 5, 7, style: 10);

        Assert.True(workspace.TryAddGeneratedChest(
            5,
            7,
            string.Empty,
            ReadOnlySpan<WorldChestItem>.Empty));
        Assert.False(workspace.TryAddGeneratedChest(
            5,
            7,
            string.Empty,
            ReadOnlySpan<WorldChestItem>.Empty));

        PlaceChestTiles(workspace.TileStore, 12, 9, style: 17);
        Assert.True(workspace.TryAddGeneratedChest(
            12,
            9,
            "Water",
            ReadOnlySpan<WorldChestItem>.Empty));

        WorldChest[] chests = workspace.CaptureGeneratedChests();
        Assert.Equal(2, chests.Length);
        Assert.Equal((short)0, chests[0].SlotId);
        Assert.Equal((short)1, chests[1].SlotId);
        Assert.Equal((5, 7), (chests[0].X, chests[0].Y));
        Assert.Equal((12, 9), (chests[1].X, chests[1].Y));
        Assert.Equal(WorldGenerationChestRules.VanillaItemSlotCount, chests[0].Items.Length);
        Assert.Equal(WorldGenerationChestRules.VanillaItemSlotCount, chests[1].Items.Length);
        Assert.All(chests[0].Items, static item => Assert.True(item.IsEmpty));
        Assert.All(chests[1].Items, static item => Assert.True(item.IsEmpty));
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedChestPlacement1458();
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
            entry.Descriptor.Id == SourceBackedChestPlacement1458.BuriedChestsId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedChestPlacement1458.WaterChestsId);
    }

    private static void PlaceChestTiles(WorldTileStore tiles, int left, int top, int style)
    {
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            var tile = new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.Containers.Value),
                Flags = WorldTileFlags.Active,
                FrameX = checked((short)(style * 36 + dx * 18)),
                FrameY = checked((short)(dy * 18)),
                LiquidKind = WorldLiquidKind.Water
            };
            tiles.SetInitialPopulationTile(left + dx, top + dy, tile);
        }
    }

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, entry => entry.Descriptor.Id.Value == id);

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private static string HashFixture(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(',')
                .Append(tile.Shape).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private static string ChestInventory(WorldChest[] chests)
    {
        var inventory = new StringBuilder();
        foreach (WorldChest chest in chests)
        {
            inventory.Append('[').Append(chest.X).Append(',').Append(chest.Y).Append(']');
            foreach (WorldChestItem item in chest.Items)
            {
                if (item.IsEmpty) continue;
                inventory.Append(item.ItemType).Append('x').Append(item.Stack);
                if (item.Prefix != 0) inventory.Append('p').Append(item.Prefix);
                inventory.Append(' ');
            }
        }
        return inventory.ToString().TrimEnd();
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

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }
}
