using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PalmTreeGrower1458Tests
{
    [Fact]
    public void Source_frames_and_short_circuit_rng_preserve_bend_and_crown()
    {
        WorldTileStore store = Site();
        var random = new ScriptedRandom(
            (10, 21, 10), (-8, 9, 2),
            (0, 3, 0), (0, 3, 0),
            (0, 13, 0), (0, 3, 1),
            (0, 13, 1), (0, 9, 0), (0, 3, 2),
            (0, 3, 0), (0, 3, 0), (0, 3, 0), (0, 3, 0), (4, 7, 6));

        Assert.True(PalmTreeGrower1458.TryGrow(store, 10, 30, random));
        Assert.Equal(14, random.Calls);
        Assert.Equal((66, 0), Frame(store, 29));
        Assert.Equal((0, 0), Frame(store, 28));
        Assert.Equal((22, 2), Frame(store, 26));
        Assert.Equal((44, 4), Frame(store, 25));
        Assert.Equal((132, 4), Frame(store, 20));
        Assert.False(store.Get(10, 19).IsActive);
        for (int y = 20; y <= 29; y++)
        {
            WorldTile tile = store.Get(10, y);
            Assert.Equal(7, tile.TileColor);
            Assert.True(tile.IsBlockInvisible);
            Assert.True(tile.IsBlockFullbright);
        }
    }

    [Theory]
    [InlineData(53)]
    [InlineData(112)]
    [InlineData(116)]
    [InlineData(234)]
    public void All_four_source_sands_admit_full_height_and_common_sapling_clearance(ushort sand)
    {
        WorldTileStore store = Site();
        Set(store, 10, 30, new WorldTile { Type = sand, Flags = WorldTileFlags.Active });
        Set(store, 10, 29, new WorldTile { Type = 20, Flags = WorldTileFlags.Active });
        Set(store, 9, 12, new WorldTile { Type = 3, Flags = WorldTileFlags.Active });
        var random = new StraightRandom();
        Assert.True(PalmTreeGrower1458.TryGrow(store, 10, 29, random));
        Assert.Equal((88, 0), Frame(store, 10));
        Assert.Equal(21, random.Calls); // Height, bend, 18 intermediate frames, crown.
        Assert.Equal(3, store.Get(9, 12).Type); // Clearance does not clear neighbouring plants.
    }

    [Theory]
    [InlineData("liquid")]
    [InlineData("wall")]
    [InlineData("slope")]
    [InlineData("half")]
    [InlineData("ground")]
    [InlineData("inactive")]
    [InlineData("high-neighbour")]
    [InlineData("trunk")]
    [InlineData("top-boundary")]
    public void Rejected_sites_do_not_mutate_tiles_or_consume_rng(string reason)
    {
        WorldTileStore store = Site();
        WorldTile ground = store.Get(10, 30);
        switch (reason)
        {
            case "liquid": Set(store, 10, 29, new WorldTile { LiquidAmount = 1 }); break;
            case "wall": Set(store, 10, 29, new WorldTile { Wall = 1 }); break;
            case "slope": ground.Shape = 2; break;
            case "half": ground.Shape = 1; break;
            case "ground": ground.Type = 1; break;
            case "inactive": ground.Flags &= ~WorldTileFlags.Active; break;
            case "high-neighbour": Set(store, 9, 10, new WorldTile { Type = 1, Flags = WorldTileFlags.Active }); break;
            case "trunk": Set(store, 10, 29, new WorldTile { Type = 1, Flags = WorldTileFlags.Active }); break;
        }
        Set(store, 10, 30, ground);
        WorldTile[] before = store.Tiles.ToArray();
        var random = new ScriptedRandom();
        Assert.False(PalmTreeGrower1458.TryGrow(store, 10, reason == "top-boundary" ? 19 : 30, random));
        Assert.Equal(0, random.Calls);
        Assert.Equal(before, store.Tiles.ToArray());
    }

    private static WorldTileStore Site()
    {
        var store = new WorldTileStore(new WorldDimensions(25, 45));
        Set(store, 10, 30, new WorldTile
        {
            Type = 53, TileColor = 7,
            Flags = WorldTileFlags.Active | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock
        });
        return store;
    }

    private static void Set(WorldTileStore store, int x, int y, WorldTile tile) => store.SetInitialPopulationTile(x, y, in tile);

    private static (int, int) Frame(WorldTileStore store, int y)
    {
        WorldTile tile = store.Get(10, y);
        Assert.True(tile.IsActive);
        Assert.Equal(323, tile.Type);
        return (tile.FrameX, tile.FrameY);
    }

    private sealed class ScriptedRandom(params (int Min, int Max, int Value)[] script) : IWorldGenerationVanillaRandom
    {
        public int Calls { get; private set; }
        public int Next(int minValue, int maxValue)
        {
            Assert.True(Calls < script.Length, "Unexpected palm RNG call");
            var expected = script[Calls++];
            Assert.Equal((expected.Min, expected.Max), (minValue, maxValue));
            return expected.Value;
        }
        public int Next(int maxValue) => Next(0, maxValue);
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }

    private sealed class StraightRandom : IWorldGenerationVanillaRandom
    {
        public int Calls { get; private set; }
        public int Next(int minValue, int maxValue)
        {
            Calls++;
            return minValue == 10 ? 20 : minValue == -8 ? 0 : minValue;
        }
        public int Next(int maxValue) => Next(0, maxValue);
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }
}
