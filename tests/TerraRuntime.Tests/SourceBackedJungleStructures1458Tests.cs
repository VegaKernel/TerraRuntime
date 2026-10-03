using TerraRuntime.Contracts.Gameplay;
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

    [Fact]
    public void First_liquid_settle_uses_quickwater_temporary_boulder_solidity_instead_of_fixed_gravity_sweeps()
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.SettleLiquids calls Liquid.QuickWater, WaterCheck and
        // ten quick-settle rounds before clearing transient liquid work.
        // While that runs, tile 137 is non-solid. The previous six direct gravity sweeps stopped above it.
        var workspace = new Workspace(48, 48);
        WorldTile water = new() { LiquidAmount = 200, LiquidKind = WorldLiquidKind.Water };
        workspace.TileStore.Set(24, 8, in water);
        SetActive(workspace, 24, 16, type: 137);
        SetActive(workspace, 24, 22, type: 1);

        JungleStructurePass1458.ApplySettleLiquidsForTesting(workspace);

        Assert.Equal((byte)0, workspace.TileStore.Get(24, 8).LiquidAmount);
        Assert.Equal((byte)0, workspace.TileStore.Get(24, 15).LiquidAmount);
        WorldTile[] tiles = workspace.TileStore.Tiles.ToArray();
        Assert.Equal(200, tiles.Sum(static tile => tile.LiquidAmount));
        Assert.Equal((byte)0, workspace.TileStore.Get(24, 8).LiquidAmount);
        Assert.Contains(tiles.Select(static (tile, index) => (tile, index)),
            static entry => entry.tile.LiquidAmount > 0 && entry.index % 48 > 16);
        Assert.True(tiles.Count(static tile => tile.LiquidAmount > 0) > 1);
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
}
