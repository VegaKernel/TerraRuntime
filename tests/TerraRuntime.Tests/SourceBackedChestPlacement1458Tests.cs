using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedChestPlacement1458Tests
{
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

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }
}
