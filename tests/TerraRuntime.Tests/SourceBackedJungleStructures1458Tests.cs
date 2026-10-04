using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedJungleStructures1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_extends_source_order_from_dirt_rock_walls_through_first_liquid_settle()
    {
        var provider = new SourceBackedJungleStructures1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "JungleStructures",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(58, builder.Entries.Count);

        string[] expectedStage =
        [
            "terraria:1.4.5.8/DirtRockWallRunner",
            "terraria:1.4.5.8/LivingTrees",
            "terraria:1.4.5.8/WoodTreeWalls",
            "terraria:1.4.5.8/Altars",
            "terraria:1.4.5.8/WetJungle",
            "terraria:1.4.5.8/JungleTemple",
            "terraria:1.4.5.8/Hives",
            "terraria:1.4.5.8/JungleChests",
            "terraria:1.4.5.8/SettleLiquids"
        ];

        int pyramids = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedDungeonPipeline1458.PyramidsId);
        Assert.True(pyramids >= 0);
        Assert.Equal(
            expectedStage,
            builder.Entries.Skip(pyramids + 1).Take(expectedStage.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string passId in expectedStage)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry biomes = Find(builder, "terraria:1.4.5.8/Biomes");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, biomes.Descriptor.RngMode);
        Assert.IsType<SourceBackedBiomesCompatibilityBarrier1458>(biomes.Pass);

        CaptureEntry caves = Find(builder, "terraria:1.4.5.8/Caves");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, caves.Descriptor.RngMode);
        Assert.IsType<SourceBackedCavesCompatibilityBarrier1458>(caves.Pass);

        CaptureEntry ores = Find(builder, "terraria:1.4.5.8/Ores");
        Assert.IsType<SourceBackedOreCompatibilityBarrier1458>(ores.Pass);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, secrets.Descriptor.RngMode);
        Assert.IsType<OrdinarySecretSeedCompatibilityBarrier1458>(secrets.Pass);
        Assert.Contains(
            SourceBackedJungleStructures1458.SettleLiquidsId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_segment_matches_source_order_from_pyramids_through_first_liquid_settle()
    {
        string[] expected =
        [
            "Dirt Rock Wall Runner",
            "Living Trees",
            "Wood Tree Walls",
            "Altars",
            "Wet Jungle",
            "Jungle Temple",
            "Hives",
            "Jungle Chests",
            "Settle Liquids"
        ];

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int pyramids = Array.IndexOf(catalog, "Pyramids");

        Assert.True(pyramids >= 0);
        Assert.Equal(expected, catalog.Skip(pyramids + 1).Take(expected.Length));
    }

    [Theory]
    [InlineData(false, "AC93BFA7C4432222F7F89E7F13CF11EC882814C13A0B79676F555EA0B9849D0F", 1288258030)]
    [InlineData(true, "CAB44932308BEBB43E542B11FA3A2D8EF38DAB7F5CB810AB8C4195729CC9F936", 1872820504)]
    public void Dirt_rock_wall_runner_full_pass_matches_official_cell_and_rng_differential(
        bool canonical, string expectedHash, int expectedNext)
    {
        int width = canonical ? 4200 : 600;
        int height = canonical ? 1200 : 500;
        double surface = canonical ? 300d : 140d;
        var workspace = new Workspace(width, height);
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var tile = new WorldTile
            {
                Type = 1,
                Wall = x >= 30 && x < width - 30 && y >= 20 && y < surface - 20 && x % 13 < 8
                    ? (ushort)2 : (ushort)1,
                Flags = WorldTileFlags.Active
            };
            workspace.TileStore.Set(x, y, in tile);
        }

        var random = new RandomAdapter(1458);
        JungleStructurePass1458.ApplyDirtRockWallRunnerForTesting(workspace, random, surface);

        Assert.Equal(expectedHash, HashOfficialCells(workspace));
        Assert.Equal(expectedNext, random.Next());
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedJungleStructures1458();
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
            entry.Descriptor.Id == SourceBackedJungleStructures1458.JungleTempleId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedJungleStructures1458.SettleLiquidsId);
    }

    [Fact]
    public void Living_tree_walls_follow_the_source_diagonal_gate_and_surface_bound()
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.LivingTreeWalls: one cardinal Living Wood cell starts the
        // offer, but the four diagonals determine it. The source writes wall 244 even when the centre is air.
        Workspace accepted = CreateLivingWallFixture(missingDiagonal: false);
        JungleStructurePass1458.ApplyWoodTreeWallsForTesting(accepted, 60d);
        Assert.Equal((ushort)244, accepted.TileStore.Get(30, 30).Wall);

        Workspace rejected = CreateLivingWallFixture(missingDiagonal: true);
        JungleStructurePass1458.ApplyWoodTreeWallsForTesting(rejected, 60d);
        Assert.Equal((ushort)0, rejected.TileStore.Get(30, 30).Wall);

        // The source loop stops strictly before worldSurface.
        WorldTile belowSurface = accepted.TileStore.Get(30, 60);
        belowSurface.Type = 191;
        belowSurface.Flags |= WorldTileFlags.Active;
        accepted.TileStore.Set(30, 60, in belowSurface);
        JungleStructurePass1458.ApplyWoodTreeWallsForTesting(accepted, 60d);
        Assert.Equal((ushort)0, accepted.TileStore.Get(30, 60).Wall);
    }

    [Theory]
    [InlineData(false, "AA898219500BC2A3221DA5B81108D2304B12F84198820E424DC5916DF27032B6")]
    [InlineData(true, "B939EFF364322474BBA0354648339F1965A65BD97E3CB3AFEEDACF192DCFF8DA")]
    public void Wood_tree_walls_full_pass_matches_official_cell_and_rng_differential(bool canonical, string expectedHash)
    {
        int width = canonical ? 4200 : 600;
        int height = canonical ? 1200 : 500;
        var workspace = new Workspace(width, height);
        for (int centerX = 100; centerX < width; centerX += 100)
        {
            int centerY = centerX % 300 == 0 ? canonical ? 300 : 140 : 60;
            foreach (int dx in new[] { -1, 1 })
            foreach (int dy in new[] { -1, 1 })
                SetActive(workspace, centerX + dx, centerY + dy, 191);
            SetActive(workspace, centerX, centerY, 191);
        }

        JungleStructurePass1458.ApplyWoodTreeWallsForTesting(workspace, canonical ? 300d : 140d);

        Assert.Equal(expectedHash, HashOfficialCells(workspace));
        Assert.Equal(906992634, new VanillaUnifiedRandom1458(1458).Next());
    }

    [Fact]
    public void Wet_jungle_waters_only_the_two_cells_above_each_columns_first_jungle_grass_tile()
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.SurfaceWaterInJungle scans from worldSurfaceLow,
        // stops at the first active tile in every column, and makes exactly the two cells above
        // a Jungle Grass hit into full water. It does not create a liquid pool or consume RNG.
        var workspace = new Workspace(80, 80);
        SetActive(workspace, 30, 40, type: 60);
        SetActive(workspace, 31, 35, type: 1);
        SetActive(workspace, 31, 40, type: 60);
        SetActive(workspace, 32, 58, type: 60);
        SetActive(workspace, 33, 59, type: 60);

        int columns = JungleStructurePass1458.ApplyWetJungleForTesting(workspace, worldSurfaceLow: 20, worldSurface: 60d);

        Assert.Equal(2, columns);
        AssertFullWater(workspace, 30, 39);
        AssertFullWater(workspace, 30, 38);
        Assert.Equal((byte)0, workspace.TileStore.Get(31, 39).LiquidAmount);
        AssertFullWater(workspace, 32, 57);
        AssertFullWater(workspace, 32, 56);
        Assert.Equal((byte)0, workspace.TileStore.Get(33, 58).LiquidAmount);
    }

    [Theory]
    [InlineData(false, "A426F087F21024799B935A69E7B47EBC5D7B8EAC7D9A97409C58F27F53A49568")]
    [InlineData(true, "5D889C2121342FDAC7D9330888C55A511E68CDA833D5EE5010FE25E8B4B59B2A")]
    public void Wet_jungle_full_pass_matches_official_cell_and_rng_differential(bool canonical, string expectedHash)
    {
        int width = canonical ? 4200 : 600;
        int height = canonical ? 1200 : 500;
        var workspace = new Workspace(width, height);
        if (canonical)
        {
            for (int x = 0; x < width; x++)
            {
                switch (x % 7)
                {
                    case 0: SetActive(workspace, x, 220, 60); break;
                    case 1:
                        SetActive(workspace, x, 215, 1);
                        SetActive(workspace, x, 220, 60);
                        break;
                    case 2: SetActive(workspace, x, 298, 60); break;
                    case 3: SetActive(workspace, x, 299, 60); break;
                    case 4: SetActive(workspace, x, 200, 60); break;
                }
            }
        }
        else
        {
            SetActive(workspace, 300, 100, 60);
            SetActive(workspace, 301, 90, 1);
            SetActive(workspace, 301, 100, 60);
            SetActive(workspace, 302, 138, 60);
            SetActive(workspace, 303, 139, 60);
        }

        JungleStructurePass1458.ApplyWetJungleForTesting(workspace,
            canonical ? 200 : 80, canonical ? 300d : 140d);

        Assert.Equal(expectedHash, HashOfficialCells(workspace));
        Assert.Equal(906992634, new VanillaUnifiedRandom1458(1458).Next());
    }

    private static string HashOfficialCells(Workspace workspace)
    {
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        var snapshot = new StringBuilder(width * height * 12);
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                .Append(tile.LiquidAmount).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    [Fact]
    public void Jungle_shrine_uses_the_source_hut_shell_cavity_wall_and_torch_offer_order()
    {
        var workspace = new Workspace(80, 80);
        var random = new SequenceRandom(30, 29, 1, 2);
        var shellBefore = new WorldTile
        {
            Type = 1,
            Wall = 88,
            FrameX = 36,
            FrameY = 54,
            Shape = 3,
            LiquidAmount = 127,
            LiquidKind = WorldLiquidKind.Lava,
            Flags = WorldTileFlags.Active
        };
        workspace.TileStore.Set(27, 27, in shellBefore);

        JungleStructurePass1458.BuildJungleShrineForTesting(workspace, 30, 30, 2, 2, hut: 119, random);

        WorldTile shell = workspace.TileStore.Get(27, 27);
        Assert.Equal((ushort)119, shell.Type);
        Assert.Equal((ushort)88, shell.Wall);
        Assert.Equal((short)36, shell.FrameX);
        Assert.Equal((short)54, shell.FrameY);
        Assert.Equal((byte)3, shell.Shape);
        Assert.Equal((byte)0, shell.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Water, shell.LiquidKind);
        WorldTile interior = workspace.TileStore.Get(31, 30);
        Assert.False(interior.IsActive);
        Assert.Equal((ushort)23, interior.Wall);
        WorldTile torch = workspace.TileStore.Get(30, 29);
        Assert.True(torch.IsActive);
        Assert.Equal((ushort)4, torch.Type);
        Assert.Equal((short)0, torch.FrameX);
        Assert.Equal((short)66, torch.FrameY);
        Assert.Equal((ushort)59, workspace.TileStore.Get(27, 34).Type);
        Assert.Equal((ushort)119, workspace.TileStore.Get(28, 26).Type);
        Assert.Equal(4, random.Calls);
    }

    [Theory]
    [InlineData(false,
        "1571,588;1931,400;1148,560;945,600;1684,722;509,608;235,653;148,703;1033,536;1513,500;1398,645",
        "2FB948C3E7B191C76C8E737BD0323A755B79103A299438AEDB403E3E018F81DA", 1005695146)]
    [InlineData(true,
        "1571,588;1931,400;619,588;52,411;807,519;1804,488;558,636;311,704;581,428;1660,669;1859,587",
        "54F4030BCB27A6BC67ECBF61214B5BDD4DE50D7A944EF2D4B1854CB42ADCC92C", 1299000437)]
    public void Jungle_shrines_canonical_full_pass_matches_official_cells_anchors_and_rng(
        bool obstructed, string expectedAnchors, string expectedHash, int expectedNext)
    {
        // Direct TerrariaServer 1.4.5.8 registered JungleShrines PassLegacy, seed 1458:
        // uniformly active Stone, with Jungle Grass replacing rows 380..749.
        var workspace = new Workspace(4200, 1200);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            var tile = new WorldTile
            {
                Type = y is >= 380 and < 750
                    ? obstructed && x is >= 900 and < 1300 && x % 19 < 3 ? (ushort)226 : (ushort)60
                    : (ushort)1,
                Wall = obstructed && x is >= 900 and < 1300 && x % 101 < 7 && y is >= 390 and < 730
                    ? (ushort)86 : (ushort)0,
                Flags = WorldTileFlags.Active
            };
            workspace.TileStore.Set(x, y, in tile);
        }
        var random = new RandomAdapter(1458);

        IReadOnlyList<WorldGenerationPoint> anchors =
            JungleStructurePass1458.ApplyJungleChestsForTesting(
                workspace, random, dungeonSide: 1, jungleHut: 119,
                worldSurface: 300d, rockLayer: 500d);

        Assert.Equal(expectedAnchors,
            string.Join(';', anchors.Select(static point => $"{point.X},{point.Y}")));
        Assert.Equal(expectedHash, HashOfficialCells(workspace));
        Assert.Equal(expectedNext, random.Next());
    }

    [Fact]
    public void First_liquid_settle_uses_quickwater_temporary_boulder_solidity_instead_of_fixed_gravity_sweeps()
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.SettleLiquids calls Liquid.QuickWater, WaterCheck and
        // ten quick-settle rounds before clearing transient liquid work.
        // While that runs, tile 137 is non-solid. The previous six direct gravity sweeps stopped above it.
        // Direct official QuickWater -> WaterCheck -> ten quick-settle rounds -> ClearPendingLiquid
        // on this 600x500 enclosed basin leaves x291..309,y121 at amount12 (sum228); next RNG906992634.
        // The former 48x48 fixture fell outside vanilla's update border and underworld evaporation bounds.
        var workspace = new Workspace(600, 500);
        WorldTile water = new() { LiquidAmount = 200, LiquidKind = WorldLiquidKind.Water };
        workspace.TileStore.Set(300, 100, in water);
        SetActive(workspace, 300, 116, type: 137);
        for (int x = 290; x <= 310; x++)
            SetActive(workspace, x, 122, type: 1);
        for (int y = 80; y <= 122; y++)
        {
            SetActive(workspace, 290, y, type: 1);
            SetActive(workspace, 310, y, type: 1);
        }

        JungleStructurePass1458.ApplySettleLiquidsForTesting(workspace);

        Assert.Equal((byte)0, workspace.TileStore.Get(300, 100).LiquidAmount);
        Assert.Equal((byte)0, workspace.TileStore.Get(300, 115).LiquidAmount);
        WorldTile[] tiles = workspace.TileStore.Tiles.ToArray();
        Assert.Equal(228, tiles.Sum(static tile => tile.LiquidAmount));
        Assert.Equal(19, tiles.Count(static tile => tile.LiquidAmount > 0));
        for (int x = 291; x <= 309; x++)
            Assert.Equal((byte)12, workspace.TileStore.Get(x, 121).LiquidAmount);
        Assert.False(workspace.TileStore.LiquidUpdates.HasPendingWork);
    }

    private static Workspace CreateLivingWallFixture(bool missingDiagonal)
    {
        var workspace = new Workspace(80, 80);
        foreach ((int x, int y) in new[] { (30, 29), (29, 29), (31, 29), (29, 31), (31, 31) })
        {
            if (missingDiagonal && (x, y) == (31, 31))
                continue;
            var livingWood = new WorldTile { Type = 191, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in livingWood);
        }
        return workspace;
    }

    private static void SetActive(Workspace workspace, int x, int y, ushort type)
    {
        var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(x, y, in tile);
    }

    private static void AssertFullWater(Workspace workspace, int x, int y)
    {
        WorldTile tile = workspace.TileStore.Get(x, y);
        Assert.Equal(byte.MaxValue, tile.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Water, tile.LiquidKind);
    }

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, entry => entry.Descriptor.Id.Value == id);

    private readonly record struct CaptureEntry(
        WorldGenerationPassDescriptor Descriptor,
        IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }

    private sealed class SequenceRandom(params int[] values) : IWorldGenerationVanillaRandom
    {
        private int next;
        public int Calls => next;
        public int Next() => throw new NotSupportedException();
        public int Next(int max) => throw new NotSupportedException();
        public int Next(int min, int max)
        {
            int value = values[next++];
            Assert.InRange(value, min, max - 1);
            return value;
        }
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] bytes) => throw new NotSupportedException();
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
