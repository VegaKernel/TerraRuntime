using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedLateStructures1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_through_floating_island_houses()
    {
        var provider = new SourceBackedLateStructures1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "LateStructures",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(78, builder.Entries.Count);

        string[] expectedStage =
        [
            "terraria:1.4.5.8/SpiderCaves",
            "terraria:1.4.5.8/GemCaves",
            "terraria:1.4.5.8/Moss",
            "terraria:1.4.5.8/Temple",
            "terraria:1.4.5.8/CaveWalls",
            "terraria:1.4.5.8/JungleTrees",
            "terraria:1.4.5.8/FloatingIslandHouses"
        ];

        int waterChests = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedChestPlacement1458.WaterChestsId);
        Assert.True(waterChests >= 0);
        Assert.Equal(
            expectedStage,
            builder.Entries.Skip(waterChests + 1).Take(expectedStage.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string passId in expectedStage)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Contains(
            SourceBackedLateStructures1458.FloatingIslandHousesId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_segment_matches_late_structure_source_order()
    {
        string[] expected =
        [
            "Spider Caves",
            "Gem Caves",
            "Moss",
            "Temple",
            "Cave Walls",
            "Jungle Trees",
            "Floating Island Houses"
        ];

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int waterChests = Array.IndexOf(catalog, "Water Chests");

        Assert.True(waterChests >= 0);
        Assert.Equal(expected, catalog.Skip(waterChests + 1).Take(expected.Length));
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedLateStructures1458();
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
            entry.Descriptor.Id == SourceBackedLateStructures1458.SpiderCavesId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedLateStructures1458.FloatingIslandHousesId);
    }


    [Fact]
    public void Cave_wall_region_count_follows_connected_enclosed_air_instead_of_an_ellipse()
    {
        var workspace = new Workspace(24, 24);
        FillStoneBox(workspace, 5, 5, 14, 14);

        WorldTile lava = workspace.TileStore.Get(9, 9);
        lava.LiquidAmount = 255;
        lava.LiquidKind = WorldLiquidKind.Lava;
        workspace.TileStore.Set(9, 9, in lava);

        CaveRegionStats1458 stats = LateStructurePass1458.CountCaveRegionForTesting(
            workspace,
            8,
            8,
            jungle: false,
            lavaOk: true);

        Assert.Equal(64, stats.Count);
        Assert.Equal(1, stats.LavaCount);
        Assert.True(stats.RockCount > 0);
    }

    [Fact]
    public void Cave_wall_spread_fills_only_the_connected_cavity_and_its_solid_boundary()
    {
        var workspace = new Workspace(24, 24);
        FillStoneBox(workspace, 5, 5, 14, 14);

        int painted = LateStructurePass1458.SpreadWallForTesting(workspace, 8, 8, 170);

        Assert.Equal(96, painted);
        for (int x = 6; x <= 13; x++)
        for (int y = 6; y <= 13; y++)
            Assert.Equal((ushort)170, workspace.TileStore.Get(x, y).Wall);

        Assert.Equal((ushort)170, workspace.TileStore.Get(5, 8).Wall);
        Assert.Equal((ushort)170, workspace.TileStore.Get(14, 8).Wall);
        Assert.Equal((ushort)0, workspace.TileStore.Get(4, 8).Wall);
        Assert.Equal((ushort)0, workspace.TileStore.Get(5, 5).Wall);
    }

    [Fact]
    public void Cave_wall_region_rejects_existing_wall_and_shimmer_like_vanilla_count_tiles()
    {
        var workspace = new Workspace(24, 24);
        FillStoneBox(workspace, 5, 5, 14, 14);

        WorldTile wall = workspace.TileStore.Get(10, 10);
        wall.Wall = 1;
        workspace.TileStore.Set(10, 10, in wall);

        CaveRegionStats1458 blocked = LateStructurePass1458.CountCaveRegionForTesting(
            workspace,
            8,
            8,
            jungle: false,
            lavaOk: true);
        Assert.Equal(1500, blocked.Count);

        wall.Wall = 0;
        wall.LiquidAmount = 255;
        wall.LiquidKind = WorldLiquidKind.Shimmer;
        workspace.TileStore.Set(10, 10, in wall);

        CaveRegionStats1458 shimmer = LateStructurePass1458.CountCaveRegionForTesting(
            workspace,
            8,
            8,
            jungle: false,
            lavaOk: true);
        Assert.Equal(1500, shimmer.Count);
    }

    private static void FillStoneBox(Workspace workspace, int left, int top, int right, int bottom)
    {
        for (int x = left; x <= right; x++)
        for (int y = top; y <= bottom; y++)
        {
            if (x != left && x != right && y != top && y != bottom)
                continue;

            var tile = new WorldTile
            {
                Type = 1,
                Flags = WorldTileFlags.Active
            };
            workspace.TileStore.Set(x, y, in tile);
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
