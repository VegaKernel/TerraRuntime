using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

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
    public void Spider_candidate_count_uses_the_official_3500_tile_ceiling()
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.SpiderCaves sets WorldGen.maxTileCount = 3500 before countTiles.
        // This 2,128-cell cavity must remain eligible for a spider cave; the former shared 1,500-cell Cave Walls
        // ceiling rejected it before candidate selection could reach Spread.Spider.
        var workspace = new Workspace(80, 60);
        FillStoneBox(workspace, 10, 10, 67, 49);

        CaveRegionStats1458 stats = LateStructurePass1458.CountSpiderCaveRegionForTesting(workspace, 30, 30);

        Assert.Equal(2128, stats.Count);
    }

    [Fact]
    public void Spider_candidate_rejection_saturates_at_its_own_3500_tile_ceiling()
    {
        var workspace = new Workspace(32, 32);
        var wall = new WorldTile { Wall = 1 };
        workspace.TileStore.Set(16, 16, in wall);

        CaveRegionStats1458 stats = LateStructurePass1458.CountSpiderCaveRegionForTesting(workspace, 16, 16);

        Assert.Equal(3500, stats.Count);
    }

    [Fact]
    public void Spider_registered_pass_exhausted_candidate_retries_match_the_official_fixture()
    {
        // TerrariaServer 1.4.5.8 PassLegacy "Spider Caves", 600x500 all-stone fixture, seed 1458:
        // three offers each exhaust width / 2 retries, leave the map unchanged, then yield 1742376249.
        var workspace = new Workspace(600, 500);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }

        workspace.TrySetLayers(140d, 200d);
        workspace.SetVanillaLiquidLines(250, 300);
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            HellChestItems = [220, 274, 275],
            TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [], ForestBackgroundStyles = []
        });
        workspace.SetVanillaBuriedChestContext(new BuriedChestContext1458
        {
            Height = workspace.HeightTiles, WorldSurface = 140d, RockLayer = 200d, LavaLine = 300,
            CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19, TungstenIsSilverTier = false,
            DesertHiveLow = 0, DesertHiveHigh = 0, HellChestItem = [220, 274, 275]
        });

        var random = new SpiderFixtureRandom(1458);
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "SpiderRetries", 1458, 600, 500);
        var context = new SpiderContext(request, workspace, random);
        new LateStructurePass1458(LateStructureStage1458.SpiderCaves, new LateStructureState1458()).Execute(context);

        Assert.Equal(1742376249, random.Next());
        Assert.Equal(0, CountSpiderWalls(workspace));
    }

    [Fact]
    public void Spider_registered_pass_accepted_candidates_match_the_official_fixture()
    {
        // TerrariaServer 1.4.5.8 PassLegacy "Spider Caves", 600x500 chamber fixture, seed 1458.
        var workspace = CreateSpiderPassWorkspace(chambers: true);
        var random = new SpiderFixtureRandom(1458);
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "SpiderCandidates", 1458, 600, 500);
        var context = new SpiderContext(request, workspace, random);

        new LateStructurePass1458(LateStructureStage1458.SpiderCaves, new LateStructureState1458()).Execute(context);

        Assert.Equal(1233190260, random.Next());
        Assert.Equal("94e55029018ef3540e7242100cf0fc7dd28d75dc868995cfac0d50aff3f92299", HashFixture(workspace));
    }

    [Fact]
    public void Temple_chest_offer_matches_the_official_direct_add_buried_chest_fixture()
    {
        // Direct TerrariaServer 1.4.5.8 AddBuriedChest(392, 340, 1293, true, 16, false, 0), with the
        // 600x500 unsafe-Lihzahrd room fixture and seed 1458, returns true and next genRand 1249478542.
        Workspace workspace = CreateTempleChestWorkspace();
        var random = new SpiderFixtureRandom(1458);
        var chests = new BuriedChest1458(workspace.TileStore, random, workspace.VanillaBuriedChestContext!);

        Assert.True(chests.TryAdd(392, 340, out int left, out int top, 1293, notNearOtherChests: true, chestStyle: 16));
        Assert.Equal((391, 349), (left, top));
        Assert.Equal(1249478542, random.Next());
        Assert.Equal("bbc1e1174d89345bbbd704333196bdde0ee55f662dd38d42779ad173739c9550", HashFixture(workspace));
    }

    [Fact]
    public void Temple_chest_offer_treats_spiky_lihzahrd_floor_as_non_solid_for_its_source_scope()
    {
        // templePart2 clears Main.tileSolid[232] around AddBuriedChest. Direct TerrariaServer 1.4.5.8 with
        // the two chest-floor cells changed to 232 rejects this offer without consuming a loot draw.
        Workspace workspace = CreateTempleChestWorkspace();
        foreach (int x in new[] { 391, 392 })
        {
            var spikyFloor = new WorldTile { Type = 232, Wall = 87, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 350, in spikyFloor);
        }
        var random = new SpiderFixtureRandom(1458);
        var chests = new BuriedChest1458(workspace.TileStore, random, workspace.VanillaBuriedChestContext!,
            spikyLihzahrdIsNonSolid: true);

        Assert.False(chests.TryAdd(392, 340, out _, out _, 1293, notNearOtherChests: true, chestStyle: 16));
        Assert.Equal(906992634, random.Next());
        Assert.Equal((ushort)232, workspace.TileStore.Get(391, 350).Type);
        Assert.Equal((ushort)232, workspace.TileStore.Get(392, 350).Type);
    }

    [Fact]
    public void Mayan_trap_rejected_open_room_prefix_matches_the_official_fixture()
    {
        // TerrariaServer 1.4.5.8 WorldGen.mayanTrap(300, 200), seed 1458, in this unsafe-Lihzahrd room:
        // it rejects before tile writes but still consumes its initial direction draw.
        Workspace workspace = CreateTempleChestWorkspace();
        var random = new SpiderFixtureRandom(1458);

        Assert.False(LateStructurePass1458.TryMayanTrapPrefixForTesting(workspace, random, 300, 200));
        Assert.Equal(1335025742, random.Next());
        Assert.Equal("b67191924607b1ae03402bf1e196ec9c4e784309b5e60ec115e28f2d93695db6", HashFixture(workspace));
    }

    [Fact]
    public void Mayan_trap_horizontal_launcher_matches_the_official_fixture()
    {
        // TerrariaServer 1.4.5.8 WorldGen.mayanTrap(300, 200), seed 1, with a 3-row temple corridor.
        // The selected right launcher writes a style-6 pressure plate and a style-1 dart trap.
        Workspace workspace = CreateMayanLauncherWorkspace();
        var random = new SpiderFixtureRandom(1);

        Assert.True(LateStructurePass1458.TryMayanTrapPrefixForTesting(workspace, random, 300, 250));
        Assert.Equal(929393559, random.Next());
        Assert.Equal("ad0397c62e56cc841ec33628a8c15b51aa49d8127fa87088727c8784aeb8b434", HashFixture(workspace));
        Assert.Equal(21, CountWireTiles(workspace, WorldTileFlags.WireGreen));
    }

    [Fact]
    public void Mayan_trap_vertical_launcher_matches_the_official_fixture()
    {
        // TerrariaServer 1.4.5.8 WorldGen.mayanTrap(300, 250), seed 2, with a Lihzahrd roof at y=200.
        Workspace workspace = CreateMayanLauncherWorkspace();
        var random = new SpiderFixtureRandom(2);

        Assert.True(LateStructurePass1458.TryMayanTrapPrefixForTesting(workspace, random, 300, 250));
        Assert.Equal(234085668, random.Next());
        Assert.Equal("5888527eda717a7767741c9d0bf796090457506b1e8e3493821038c3f59eb97c", HashFixture(workspace));
        Assert.Equal(150, CountWireTiles(workspace, WorldTileFlags.WireBlue));
    }

    [Fact]
    public void Mayan_trap_close_horizontal_launcher_matches_the_official_style_two_fixture()
    {
        // TerrariaServer 1.4.5.8 mayanTrap(300,250), seed 1, with a six-cell corridor: the close launcher
        // takes its extra style roll and uses type-137 style 2 (frame Y 36), not the ordinary style 1.
        Workspace workspace = CreateMayanCloseLauncherWorkspace();
        var random = new SpiderFixtureRandom(1);

        Assert.True(LateStructurePass1458.TryMayanTrapPrefixForTesting(workspace, random, 300, 250));
        Assert.Equal(760389092, random.Next());
        Assert.Equal("e9e91894f08fb1131c7018eb00e3f05c9d6a496fbc9990bae24ec0f660a97727", HashFixture(workspace));
        Assert.Equal(7, CountWireTiles(workspace, WorldTileFlags.WireGreen));
    }

    [Fact]
    public void Mayan_trap_reuses_the_existing_pressure_plate_wire_colour()
    {
        // TerrariaServer 1.4.5.8 still consumes the random colour draw, then lets an existing plate wire
        // override it. Seed 1's ordinary choice is green, but this red fixture must write only red wire.
        Workspace workspace = CreateMayanLauncherWorkspace();
        WorldTile trigger = workspace.TileStore.Get(300, 349);
        trigger.Flags |= WorldTileFlags.WireRed;
        workspace.TileStore.Set(300, 349, in trigger);
        var random = new SpiderFixtureRandom(1);

        Assert.True(LateStructurePass1458.TryMayanTrapPrefixForTesting(workspace, random, 300, 250));
        Assert.Equal(929393559, random.Next());
        Assert.Equal(21, CountWireTiles(workspace, WorldTileFlags.WireRed));
        Assert.Equal(0, CountWireTiles(workspace, WorldTileFlags.WireBlue));
        Assert.Equal(0, CountWireTiles(workspace, WorldTileFlags.WireGreen));
    }

    [Fact]
    public void Temple_chair_offer_matches_the_official_direct_place_tile_fixture()
    {
        // Direct TerrariaServer 1.4.5.8 WorldGen.PlaceTile(300, 349, 15, true, false, -1, 12)
        // in this unsafe-Lihzahrd room returns true and writes exactly a 1x2 chair at y=348..349.
        Workspace workspace = CreateTempleChestWorkspace();

        Assert.True(LateStructurePass1458.TryPlaceTempleChairForTesting(workspace, 300, 349));
        Assert.False(workspace.TileStore.Get(299, 348).IsActive);
        AssertChairTile(workspace, 300, 348, 480);
        AssertChairTile(workspace, 300, 349, 498);
        Assert.False(workspace.TileStore.Get(301, 348).IsActive);
    }

    [Fact]
    public void Temple_table_offer_uses_the_official_centered_three_by_two_footprint()
    {
        // Direct TerrariaServer 1.4.5.8 PlaceTile(201, 349, 14, true, false, -1, 9) writes x=200..202.
        Workspace workspace = CreateTempleChestWorkspace();

        Assert.True(LateStructurePass1458.TryPlaceTempleTableForTesting(workspace, 201, 349));
        for (int x = 200; x <= 202; x++)
        {
            AssertTempleObjectTile(workspace, x, 348, 14, (short)(486 + (x - 200) * 18), 0);
            AssertTempleObjectTile(workspace, x, 349, 14, (short)(486 + (x - 200) * 18), 18);
        }
    }

    [Fact]
    public void Temple_painting_offer_uses_the_official_wall_centered_three_by_three_footprint()
    {
        // Direct TerrariaServer 1.4.5.8 PlaceTile(294, 239, 240, true, false, -1, 88) needs backing wall,
        // not floor support, and writes x=293..295/y=238..240.
        Workspace workspace = CreateTempleChestWorkspace();

        Assert.True(LateStructurePass1458.TryPlaceTemplePaintingForTesting(workspace, 294, 239));
        for (int x = 293; x <= 295; x++)
        for (int y = 238; y <= 240; y++)
            AssertTempleObjectTile(workspace, x, y, 240, (short)(864 + (x - 293) * 18), (short)(108 + (y - 238) * 18));
    }

    [Fact]
    public void Temple_candle_offer_uses_the_official_two_by_three_frames()
    {
        // Direct TerrariaServer 1.4.5.8 PlaceTile(205, 349, 105, true, false, -1, 45).
        Workspace workspace = CreateTempleChestWorkspace();

        Assert.True(LateStructurePass1458.TryPlaceTempleCandleForTesting(workspace, 205, 349));
        for (int x = 205; x <= 206; x++)
        for (int y = 347; y <= 349; y++)
            AssertTempleObjectTile(workspace, x, y, 105, (short)(1620 + (x - 205) * 18), (short)((y - 347) * 18));
    }

    [Fact]
    public void Temple_work_bench_offer_uses_the_official_two_by_one_frames()
    {
        // Direct TerrariaServer 1.4.5.8 PlaceTile(353, 349, 18, true, false, -1, 10).
        Workspace workspace = CreateTempleChestWorkspace();

        Assert.True(LateStructurePass1458.TryPlaceTempleWorkBenchForTesting(workspace, 353, 349));
        AssertTempleObjectTile(workspace, 353, 349, 18, 360, 0);
        AssertTempleObjectTile(workspace, 354, 349, 18, 378, 0);
    }

    [Theory]
    [InlineData(1458, 725285729, "814231d07b6ce34b009a9d0d8ca832e4ed136ec2869374940c05439d92ad6c6c")]
    [InlineData(1, 1439592459, "3926f158c33add13d94aebda89c6d67541894b64e227368463884d1bbdf66419")]
    [InlineData(2, 796227867, "5a4aa406d7afd78488d42091f590e6c3004c17ffe0e64a33c63d69f1b0ed4c72")]
    [InlineData(777, 1736825953, "6fbc39f0ec9fab94cc07db00dd41f9c59a0a2efd5a1a6c3b6b56517128c53e4e")]
    public void Temple_part_two_full_fixture_matches_the_official_delegate(int seed, int expectedNext, string expectedHash)
    {
        Workspace workspace = CreateTempleChestWorkspace();
        workspace.SetVanillaTemplePart2State(new VanillaTemplePart2State1458(200, 400, 100, 350, 1));
        var random = new SpiderFixtureRandom(seed);
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "TempleDiagnostic", (ulong)seed, 600, 500);
        var context = new SpiderContext(request, workspace, random);

        LateStructurePass1458.ApplyTemplePart2ForTesting(context, workspace, 140d);

        Assert.Equal(expectedNext, random.Next());
        Assert.Equal(expectedHash, HashFixture(workspace));
    }

    [Fact]
    public void Temple_registered_late_structure_pass_matches_the_official_delegate_fixture()
    {
        // The same official 1.4.5.8 templePart2 fixture, but through the production late-pass boundary.
        // This guards the source GenVars handoff and prevents a later return to material-map discovery.
        Workspace workspace = CreateTempleChestWorkspace();
        workspace.SetVanillaTemplePart2State(new VanillaTemplePart2State1458(200, 400, 100, 350, 1));
        var random = new SpiderFixtureRandom(1458);
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "TempleRegistered", 1458, 600, 500);
        var context = new SpiderContext(request, workspace, random);

        new LateStructurePass1458(LateStructureStage1458.Temple, new LateStructureState1458()).Execute(context);

        Assert.Equal(725285729, random.Next());
        Assert.Equal("814231d07b6ce34b009a9d0d8ca832e4ed136ec2869374940c05439d92ad6c6c", HashFixture(workspace));
    }

    private static Workspace CreateSpiderPassWorkspace(bool chambers)
    {
        var workspace = new Workspace(600, 500);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }

        if (chambers)
        {
            for (int left = 200; left < 400; left += 20)
            for (int x = left + 1; x < left + 19; x++)
            for (int y = 160; y < 300; y++)
            {
                var empty = new WorldTile();
                workspace.TileStore.Set(x, y, in empty);
            }
        }

        workspace.TrySetLayers(140d, 200d);
        workspace.SetVanillaLiquidLines(250, 300);
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            HellChestItems = [220, 274, 275],
            TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [], ForestBackgroundStyles = []
        });
        workspace.SetVanillaBuriedChestContext(new BuriedChestContext1458
        {
            Height = workspace.HeightTiles, WorldSurface = 140d, RockLayer = 200d, LavaLine = 300,
            CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19, TungstenIsSilverTier = false,
            DesertHiveLow = 0, DesertHiveHigh = 0, HellChestItem = [220, 274, 275]
        });
        return workspace;
    }

    private static Workspace CreateTempleChestWorkspace()
    {
        Workspace workspace = CreateSpiderPassWorkspace(chambers: false);
        for (int x = 200; x < 400; x++)
        for (int y = 100; y < 350; y++)
        {
            var emptyTemple = new WorldTile { Wall = 87 };
            workspace.TileStore.Set(x, y, in emptyTemple);
        }
        return workspace;
    }

    private static Workspace CreateMayanLauncherWorkspace()
    {
        var workspace = new Workspace(600, 800);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }
        for (int x = 200; x < 400; x++)
        for (int y = 100; y < 350; y++)
        {
            var emptyTemple = new WorldTile { Wall = 87 };
            workspace.TileStore.Set(x, y, in emptyTemple);
        }
        for (int y = 347; y <= 349; y++)
        foreach (int x in new[] { 280, 320 })
        {
            var wall = new WorldTile { Type = 1, Wall = 87, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in wall);
        }
        var verticalAnchor = new WorldTile { Type = 1, Wall = 87, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 200, in verticalAnchor);
        return workspace;
    }

    private static Workspace CreateMayanCloseLauncherWorkspace()
    {
        Workspace workspace = CreateMayanLauncherWorkspace();
        for (int y = 347; y <= 349; y++)
        foreach (int x in new[] { 280, 320 })
        {
            var empty = new WorldTile { Wall = 87 };
            workspace.TileStore.Set(x, y, in empty);
        }
        for (int y = 347; y <= 349; y++)
        foreach (int x in new[] { 294, 306 })
        {
            var anchor = new WorldTile { Type = 1, Wall = 87, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in anchor);
        }
        return workspace;
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
    public void Spider_frontier_matches_the_official_spread_fixture()
    {
        const int width = 300;
        const int height = 500;
        var workspace = new Workspace(width, height);
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }

        for (int x = 120; x < 180; x++)
        for (int y = 120; y < 400; y++)
        {
            var empty = new WorldTile { FrameX = 0, FrameY = 0 };
            workspace.TileStore.Set(x, y, in empty);
        }

        var random = new SpiderFixtureRandom(1458);
        LateStructurePass1458.SpreadSpiderForTesting(workspace, random, 140d, 200d, 150, 150);

        int runtimeSpiderWalls = CountSpiderWalls(workspace);
        Assert.True(
            random.Next() == 1713827842,
            $"next={random.Last} calls={random.Calls} walls={runtimeSpiderWalls} wallHash={HashSpiderWallCoordinates(workspace)}");
        Assert.Equal("65ff55b08dd1b35abb0962924f894b16429ff8767e88e6e7bf9d2cbec789a0ce", HashFixture(workspace));
    }

    [Fact]
    public void Gem_cave_solid_frontier_matches_the_official_fixture()
    {
        var workspace = new Workspace(600, 500);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }

        var random = new SpiderFixtureRandom(1458);
        LateStructurePass1458.ApplyGemCaveForTesting(workspace, random, 300, 250);

        Assert.Equal(2010695125, random.Next());
        Assert.Equal("b04ad6f6d983518e472824f081fe3df558b75d2fe6109f9eff1b857af50df626", HashFixture(workspace));
    }

    [Fact]
    public void Gem_cave_open_frontier_matches_the_official_fixture()
    {
        var workspace = new Workspace(600, 500);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }
        for (int x = 296; x < 304; x++)
        for (int y = 246; y < 254; y++)
        {
            var empty = new WorldTile();
            workspace.TileStore.Set(x, y, in empty);
        }

        var random = new SpiderFixtureRandom(1458);
        LateStructurePass1458.ApplyGemCaveForTesting(workspace, random, 300, 250);

        Assert.True(random.Next() == 1573606171,
            $"next={random.Last} calls={random.Calls} objects={CountActiveType(workspace, 178)} coords={CaptureTypeCoordinates(workspace, 178)}");
        Assert.Equal("5688f0564ed790a77805df6963808d1433425a55a46a3888a27ac7528ff4decd", HashFixture(workspace));
    }

    [Fact]
    public void Spider_floor_decorations_match_the_official_spread_fixture()
    {
        const int width = 300;
        const int height = 500;
        var workspace = new Workspace(width, height);
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }

        // The solid row at 300 activates the source's pot/web/pile offers on the lower frontier.
        for (int x = 120; x < 180; x++)
        for (int y = 120; y < 300; y++)
        {
            var empty = new WorldTile { FrameX = 0, FrameY = 0 };
            workspace.TileStore.Set(x, y, in empty);
        }

        var random = new SpiderFixtureRandom(1458);
        LateStructurePass1458.SpreadSpiderForTesting(workspace, random, 140d, 200d, 150, 150);

        int cobwebs = CountActiveType(workspace, 187);
        int pots = CountActiveType(workspace, 28);
        int piles = CountActiveType(workspace, 185);
        Assert.True(random.Next() == 2013149343, $"next={random.Last} calls={random.Calls} cobwebs={cobwebs} pots={pots} piles={piles}");
        Assert.Equal("092bb7909a70e01dcf33bd35fea1cd8bb8a780c64e3cdf1201d20ea1f25a0c49", HashFixture(workspace));
    }

    [Fact]
    public void Spider_chest_offer_matches_the_official_spread_fixture()
    {
        const int width = 300;
        const int height = 500;
        var workspace = new Workspace(width, height);
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }

        for (int x = 120; x < 180; x++)
        for (int y = 120; y < 300; y++)
        {
            var empty = new WorldTile { FrameX = 0, FrameY = 0 };
            workspace.TileStore.Set(x, y, in empty);
        }

        var random = new SpiderFixtureRandom(11);
        LateStructurePass1458.SpreadSpiderForTesting(workspace, random, 140d, 200d, 150, 150);

        Assert.True(random.Next() == 1710437579, $"next={random.Last} calls={random.Calls}");
        Assert.Equal("6e30a3f9946c67416adc4c6290a98493411d57fe52e51d469a30f8f4b237e669", HashFixture(workspace));
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

    [Fact]
    public void Floating_island_house_rejects_a_registered_container_on_its_sunplate_perimeter()
    {
        var workspace = new Workspace(48, 32);
        const int left = 10;
        const int floorY = 20;

        for (int x = left; x <= left + 12; x++)
        {
            var floor = new WorldTile { Type = 189, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, floorY, in floor);
        }

        var chestAnchor = new WorldTile
        {
            Type = 21,
            Flags = WorldTileFlags.Active,
            FrameX = 0,
            FrameY = 0
        };
        workspace.TileStore.Set(left + 3, floorY, in chestAnchor);

        Assert.False(LateStructurePass1458.CanBuildSkyHouseForTesting(workspace, left, floorY));
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

    private static string HashFixture(Workspace workspace)
    {
        var snapshot = new StringBuilder();
        for (int x = 0; x < workspace.TileStore.Dimensions.WidthTiles; x++)
        for (int y = 0; y < workspace.TileStore.Dimensions.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                .Append(tile.Type).Append(',')
                .Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',')
                .Append(tile.FrameY).Append(',')
                .Append(tile.LiquidAmount).Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private static int CountSpiderWalls(Workspace workspace)
    {
        int count = 0;
        for (int x = 0; x < workspace.TileStore.Dimensions.WidthTiles; x++)
        for (int y = 0; y < workspace.TileStore.Dimensions.HeightTiles; y++)
            count += workspace.TileStore.Get(x, y).Wall == 62 ? 1 : 0;
        return count;
    }

    private static int CountActiveType(Workspace workspace, ushort type)
    {
        int count = 0;
        for (int x = 0; x < workspace.TileStore.Dimensions.WidthTiles; x++)
        for (int y = 0; y < workspace.TileStore.Dimensions.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            count += tile.IsActive && tile.Type == type ? 1 : 0;
        }
        return count;
    }

    private static int CountWireTiles(Workspace workspace, WorldTileFlags wire)
    {
        int count = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
            count += (workspace.TileStore.Get(x, y).Flags & wire) != 0 ? 1 : 0;
        return count;
    }

    private static void AssertChairTile(Workspace workspace, int x, int y, short frameY)
    {
        WorldTile tile = workspace.TileStore.Get(x, y);
        Assert.True(tile.IsActive);
        Assert.Equal((ushort)15, tile.Type);
        Assert.Equal((short)0, tile.FrameX);
        Assert.Equal(frameY, tile.FrameY);
        Assert.Equal((ushort)87, tile.Wall);
    }

    private static void AssertTempleObjectTile(Workspace workspace, int x, int y, ushort type, short frameX, short frameY)
    {
        WorldTile tile = workspace.TileStore.Get(x, y);
        Assert.True(tile.IsActive);
        Assert.Equal(type, tile.Type);
        Assert.Equal(frameX, tile.FrameX);
        Assert.Equal(frameY, tile.FrameY);
        Assert.Equal((ushort)87, tile.Wall);
    }

    private static string CaptureTypeCoordinates(Workspace workspace, ushort type)
    {
        var coordinates = new StringBuilder();
        for (int x = 0; x < workspace.TileStore.Dimensions.WidthTiles; x++)
        for (int y = 0; y < workspace.TileStore.Dimensions.HeightTiles; y++)
            if (workspace.TileStore.Get(x, y) is { IsActive: true, Type: var found } && found == type)
                coordinates.Append(x).Append(',').Append(y).Append(';');
        return coordinates.ToString();
    }

    private static string HashSpiderWallCoordinates(Workspace workspace)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CaptureSpiderWallCoordinates(workspace))));
    }

    private static string CaptureSpiderWallCoordinates(Workspace workspace)
    {
        var coordinates = new StringBuilder();
        for (int x = 0; x < workspace.TileStore.Dimensions.WidthTiles; x++)
        for (int y = 0; y < workspace.TileStore.Dimensions.HeightTiles; y++)
            if (workspace.TileStore.Get(x, y).Wall == 62)
                coordinates.Append(x).Append(',').Append(y).Append(';');
        return coordinates.ToString();
    }

    private sealed class SpiderFixtureRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Calls { get; private set; }
        public int Last { get; private set; }
        public int Next() => Record(random.Next());
        public int Next(int max) => Record(random.Next(max));
        public int Next(int min, int max) => Record(random.Next(min, max));
        public double NextDouble() { Calls++; return random.NextDouble(); }
        public void NextBytes(byte[] bytes) { Calls += bytes.Length; random.NextBytes(bytes); }
        private int Record(int value) { Calls++; Last = value; return value; }
    }

    private sealed class SpiderContext(
        WorldGenerationRequest request,
        Workspace workspace,
        IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
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
