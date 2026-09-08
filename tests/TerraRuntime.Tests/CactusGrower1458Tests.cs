using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class CactusGrower1458Tests
{
    [Fact]
    public void Source_growth_creates_trunk_and_raised_arm_with_paint_and_no_cosmetic_rng()
    {
        WorldTileStore store = Site();
        var random = new ScriptedRandom((0, 2, 0), (11, 13, 11),
            (11, 13, 11), (0, 3, 2), (2, 8, 7), (11, 13, 11), (0, 3, 0), (11, 13, 11));
        Assert.True(CactusGrower1458.TryGrow(store, 70, 60, random));
        Assert.True(CactusGrower1458.TryGrow(store, 70, 59, random));
        Assert.True(CactusGrower1458.TryGrow(store, 70, 58, random));
        Assert.True(CactusGrower1458.TryGrow(store, 70, 58, random));
        Assert.True(CactusGrower1458.TryGrow(store, 69, 58, random));
        Assert.Equal(8, random.Calls);
        foreach (var (x, y) in new[] { (70, 59), (70, 58), (70, 57), (69, 58), (69, 57) })
        {
            WorldTile tile = store.Get(x, y);
            Assert.True(tile.IsActive);
            Assert.Equal(80, tile.Type);
            Assert.Equal(7, tile.TileColor);
            Assert.True(tile.IsBlockInvisible);
            Assert.True(tile.IsBlockFullbright);
            Assert.Equal(0, tile.FrameX); // TileFrameCosmetic is skipped during generation.
            Assert.Equal(0, tile.FrameY);
        }
        Assert.False(store.Get(69, 59).IsActive); // Side arm does not grow down into a second root.
    }

    [Theory]
    [InlineData(53)]
    [InlineData(112)]
    [InlineData(116)]
    [InlineData(234)]
    public void Root_admits_only_the_verified_conversion_sands(ushort sand)
    {
        WorldTileStore store = Site(sand);
        Assert.True(CactusGrower1458.TryGrow(store, 70, 60, new ScriptedRandom((0, 2, 0))));
    }

    [Theory]
    [InlineData(254, true)]
    [InlineData(255, false)]
    public void Water_gate_uses_integer_units_and_strict_greater_than_twenty_five(int lastAmount, bool grows)
    {
        WorldTileStore store = Site();
        for (int row = 40; row < 66; row++)
            At(store, 90, row).LiquidAmount = (byte)(row == 65 ? lastAmount : 255);
        var random = new ScriptedRandom(grows ? [(0, 2, 0)] : []);
        Assert.Equal(grows, CactusGrower1458.TryGrow(store, 70, 60, random));
        Assert.Equal(grows ? 1 : 0, random.Calls);
    }

    [Theory]
    [InlineData("half")]
    [InlineData("actuated")]
    [InlineData("wrong-ground")]
    [InlineData("wet-top")]
    [InlineData("blocked-side")]
    [InlineData("nearby-cacti")]
    [InlineData("insufficient-sand")]
    public void Invalid_sites_do_not_consume_growth_rng_or_mutate(string reason)
    {
        WorldTileStore store = Site();
        switch (reason)
        {
            case "half": At(store, 70, 60).Shape = 1; break;
            case "actuated": At(store, 70, 60).Flags |= WorldTileFlags.Inactive; break;
            case "wrong-ground": At(store, 70, 60).Type = 397; break;
            case "wet-top": At(store, 70, 59).LiquidAmount = 1; break;
            case "blocked-side": At(store, 69, 59) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; break;
            case "nearby-cacti":
                for (int column = 64; column < 68; column++)
                    At(store, column, 59) = new WorldTile { Type = 80, Flags = WorldTileFlags.Active };
                break;
            case "insufficient-sand":
                for (int column = 64; column <= 76; column++)
                for (int row = 60; row <= 61; row++)
                    if (column != 70 || row != 60) At(store, column, row) = default;
                break;
        }
        WorldTile[] before = store.Tiles.ToArray();
        Assert.False(CactusGrower1458.TryGrow(store, 70, 60, new ScriptedRandom()));
        Assert.Equal(before, store.Tiles.ToArray());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Generation_slope_roll_precedes_check_cactus_support_rejection(int roll, bool grows)
    {
        WorldTileStore store = Site();
        At(store, 70, 60).Shape = 2;
        Assert.Equal(grows, CactusGrower1458.TryGrow(store, 70, 60, new ScriptedRandom((0, 2, roll))));
        Assert.Equal(grows ? 0 : 2, store.Get(70, 60).Shape);
        Assert.Equal(grows, store.Get(70, 59).IsActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plant_keeps_all_150_candidate_pairs_even_after_initial_rejection(bool valid)
    {
        WorldTileStore store = Site();
        if (!valid) At(store, 70, 60).Type = 1;
        var script = new List<(int, int, int)>();
        if (valid) script.Add((0, 2, 0));
        for (int i = 0; i < 150; i++)
        {
            script.Add((69, 72, 69));
            script.Add((50, 62, 50));
        }
        var random = new ScriptedRandom(script.ToArray());
        Assert.Equal(valid, CactusGrower1458.Plant(store, 70, 60, random));
        Assert.Equal(valid ? 301 : 300, random.Calls);
    }

    private static WorldTileStore Site(ushort sand = 53)
    {
        var store = new WorldTileStore(new WorldDimensions(140, 100));
        for (int column = 20; column < 120; column++)
        for (int row = 60; row <= 61; row++)
            At(store, column, row) = new WorldTile { Type = sand, TileColor = 7,
                Flags = WorldTileFlags.Active | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock };
        return store;
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

    private sealed class ScriptedRandom(params (int Min, int Max, int Value)[] script) : IWorldGenerationVanillaRandom
    {
        public int Calls { get; private set; }
        public int Next(int minValue, int maxValue)
        {
            Assert.True(Calls < script.Length, "Unexpected cactus RNG call");
            var draw = script[Calls++];
            Assert.Equal((draw.Min, draw.Max), (minValue, maxValue));
            return draw.Value;
        }
        public int Next(int maxValue) => Next(0, maxValue);
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }
}
