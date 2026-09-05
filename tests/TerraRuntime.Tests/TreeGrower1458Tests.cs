using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTreeGrower1458Tests
{
    [Fact]
    public void Scripted_source_path_pins_trunk_branches_roots_and_leafy_top_frames()
    {
        WorldTileStore store = CreateTreeSite();
        var random = new ScriptedRandom(
            7,
            0, 7,
            1, 5, 2, 1,
            2, 5, 6, 0, 2,
            0, 7, 7, 3,
            1, 7, 0, 0, 1, 2,
            2, 1,
            0, 9,
            0, 1, 2, 0,
            12, 1);

        bool grown = TreeGrower1458.TryGrow(store, x: 10, checkedY: 30, random);

        Assert.True(grown);
        Assert.Equal(32, random.CallCount);
        Assert.Equal((22, 220), Frame(store, 10, 23));
        Assert.Equal((88, 22), Frame(store, 10, 24));
        Assert.Equal((44, 242), Frame(store, 9, 24));
        Assert.Equal((66, 110), Frame(store, 10, 25));
        Assert.Equal((88, 66), Frame(store, 11, 25));
        Assert.Equal((44, 66), Frame(store, 10, 26));
        Assert.Equal((110, 88), Frame(store, 10, 27));
        Assert.Equal((44, 198), Frame(store, 9, 27));
        Assert.Equal((88, 88), Frame(store, 11, 27));
        Assert.Equal((0, 110), Frame(store, 10, 28));
        Assert.Equal((88, 132), Frame(store, 10, 29));
        Assert.Equal((22, 154), Frame(store, 11, 29));
        Assert.Equal((44, 176), Frame(store, 9, 29));

        for (int y = 23; y <= 29; y++)
            AssertTreeColorAndCoating(store.Get(10, y));
        AssertTreeColorAndCoating(store.Get(9, 24));
        AssertTreeColorAndCoating(store.Get(11, 25));
    }

    [Fact]
    public void Consecutive_branch_rerolls_preserve_the_shared_rng_order()
    {
        WorldTileStore store = CreateTreeSite();
        var random = new ScriptedRandom(
            5,
            0, 0,
            0, 5, 0, 0,
            0, 5, 5, 6, 0, 0,
            0, 6, 6, 2,
            0, 0,
            2, 0, 0,
            12, 0);

        Assert.True(TreeGrower1458.TryGrow(store, 10, 30, random));

        Assert.Equal(24, random.CallCount);
        Assert.True(store.Get(9, 26).IsActive);
        Assert.False(store.Get(9, 27).IsActive);
        Assert.True(store.Get(11, 27).IsActive);
        Assert.False(store.Get(11, 28).IsActive);
    }

    [Fact]
    public void Growth_gates_reject_liquid_shape_neighbors_wall_and_obstruction_at_source_points()
    {
        AssertRejected(
            static store => SetTile(store, 10, 29, type: 0, active: false, liquid: 1),
            expectedRandomCalls: 0);
        AssertRejected(
            static store => SetTile(store, 10, 30, type: 2, active: true, shape: 1),
            expectedRandomCalls: 0);
        AssertRejected(
            static store => SetTile(
                store,
                10,
                30,
                type: 2,
                active: true,
                flags: WorldTileFlags.Inactive),
            expectedRandomCalls: 0);
        AssertRejected(
            static store =>
            {
                SetTile(store, 9, 30, type: 0, active: false);
                SetTile(store, 11, 30, type: 0, active: false);
            },
            expectedRandomCalls: 0);
        AssertRejected(
            static store => SetTile(store, 10, 29, type: 0, active: false, wall: 1),
            expectedRandomCalls: 0);
        AssertRejected(
            static store => SetTile(store, 10, 24, type: 1, active: true),
            expectedRandomCalls: 1);
    }

    [Fact]
    public void Source_replaceable_plants_do_not_block_the_tree_envelope()
    {
        WorldTileStore store = CreateTreeSite();
        SetTile(store, 10, 26, type: 3, active: true);
        SetTile(store, 9, 25, type: 20, active: true);
        var random = new ScriptedRandom(
            5,
            0, 0,
            0, 0,
            0, 0,
            0, 0,
            0, 0,
            2, 0, 0,
            12, 0);

        Assert.True(TreeGrower1458.TryGrow(store, 10, 30, random));
        Assert.Equal((22, 198), Frame(store, 10, 25));
        Assert.Equal((0, 0), Frame(store, 10, 26));
    }

    [Fact]
    public void Growth_catalog_pins_complete_source_capability_sets()
    {
        Assert.Equal(12, CountTiles(TreeGrowthCatalog1458.IsTreeGround));
        Assert.Equal(4, CountTiles(TreeGrowthCatalog1458.IsCommonSapling));
        Assert.Equal(27, CountTiles(TreeGrowthCatalog1458.IsReplaceableGrowthTile));
        Assert.Equal(27, CountWalls(TreeGrowthCatalog1458.AllowsPlantGrowth));

        Assert.True(TreeGrowthCatalog1458.IsTreeGround(VanillaTileIds.Grass));
        Assert.True(TreeGrowthCatalog1458.IsTreeGround(VanillaTileIds.JungleGrass));
        Assert.True(TreeGrowthCatalog1458.IsTreeGround(VanillaTileIds.MushroomGrass));
        Assert.True(TreeGrowthCatalog1458.IsTreeGround(VanillaTileIds.SnowBlock));
        Assert.False(TreeGrowthCatalog1458.IsTreeGround(VanillaTileIds.Dirt));
        Assert.True(TreeGrowthCatalog1458.AllowsPlantGrowth(VanillaWallIds.None));
        Assert.False(TreeGrowthCatalog1458.AllowsPlantGrowth(VanillaWallIds.Stone));
    }

    private static void AssertRejected(Action<WorldTileStore> arrange, int expectedRandomCalls)
    {
        WorldTileStore store = CreateTreeSite();
        arrange(store);
        var random = new ScriptedRandom(7);

        Assert.False(TreeGrower1458.TryGrow(store, 10, 30, random));
        Assert.Equal(expectedRandomCalls, random.CallCount);
        Assert.DoesNotContain(
            store.Tiles.ToArray(),
            static tile => tile.IsActive && tile.TileType == VanillaTileIds.Trees);
    }

    private static int CountTiles(Func<TileTypeId, bool> predicate)
    {
        int count = 0;
        for (int value = 0; value < VanillaTileIds.Count; value++)
            count += predicate(new TileTypeId(value)) ? 1 : 0;
        return count;
    }

    private static int CountWalls(Func<WallTypeId, bool> predicate)
    {
        int count = 0;
        for (int value = 0; value < VanillaWallIds.Count; value++)
            count += predicate(new WallTypeId(value)) ? 1 : 0;
        return count;
    }

    private static WorldTileStore CreateTreeSite()
    {
        var store = new WorldTileStore(new WorldDimensions(40, 40));
        for (int x = 9; x <= 11; x++)
        {
            SetTile(
                store,
                x,
                30,
                type: 2,
                active: true,
                color: 7,
                flags: WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        }
        return store;
    }

    private static void SetTile(
        WorldTileStore store,
        int x,
        int y,
        ushort type,
        bool active,
        byte liquid = 0,
        ushort wall = 0,
        byte shape = 0,
        byte color = 0,
        WorldTileFlags flags = WorldTileFlags.None)
    {
        var tile = new WorldTile
        {
            Type = type,
            Wall = wall,
            Shape = shape,
            LiquidAmount = liquid,
            TileColor = color,
            Flags = flags | (active ? WorldTileFlags.Active : WorldTileFlags.None)
        };
        store.SetInitialPopulationTile(x, y, in tile);
    }

    private static (short X, short Y) Frame(WorldTileStore store, int x, int y)
    {
        WorldTile tile = store.Get(x, y);
        Assert.True(tile.IsActive);
        Assert.Equal(VanillaTileIds.Trees, tile.TileType);
        return (tile.FrameX, tile.FrameY);
    }

    private static void AssertTreeColorAndCoating(WorldTile tile)
    {
        Assert.Equal((byte)7, tile.TileColor);
        Assert.True(tile.IsBlockInvisible);
        Assert.True(tile.IsBlockFullbright);
    }

    private sealed class ScriptedRandom(params int[] values) : IWorldGenerationVanillaRandom
    {
        private int index;

        public int CallCount => index;

        public int Next() => Take(int.MinValue, int.MaxValue);

        public int Next(int maxValue) => Take(0, maxValue);

        public int Next(int minValue, int maxValue) => Take(minValue, maxValue);

        public double NextDouble() => throw new NotSupportedException();

        public void NextBytes(byte[] buffer) => throw new NotSupportedException();

        private int Take(int minimum, int maximum)
        {
            Assert.True(index < values.Length, "Tree grower consumed more RNG calls than the source script provides.");
            int value = values[index++];
            Assert.InRange(value, minimum, maximum - 1);
            return value;
        }
    }
}
