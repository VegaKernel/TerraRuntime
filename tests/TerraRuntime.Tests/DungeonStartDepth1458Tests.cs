using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class DungeonStartDepth1458Tests
{
    [Theory]
    [InlineData(1, 0, false, 259)]
    [InlineData(1, 1, false, 425)]
    [InlineData(1, 2, false, 425)]
    [InlineData(1, 3, false, 425)]
    [InlineData(1, 4, false, 425)]
    [InlineData(1, 5, false, 425)]
    [InlineData(1, 0, true, 425)]
    [InlineData(19, 0, false, 425)]
    public void Initial_depth_uses_SolidTile_not_active(int type, byte shape, bool actuated, int expected)
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 600));
        for (int y = 200; y < 600; y++)
            tiles.Set(40, y, new WorldTile { Type = (ushort)type, Shape = shape,
                Flags = WorldTileFlags.Active | (actuated ? WorldTileFlags.Inactive : 0) });
        var random = new DepthRandom(0);
        Assert.Equal(expected, DungeonGraphGenerator1458.ResolveStartY(tiles, 40, 150, 300, random));
        Assert.Equal(1, random.Draws);
    }

    [Fact]
    public void Source_search_may_start_outside_world_without_an_invented_depth_clamp()
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 400));
        tiles.Set(40, 10, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var random = new DepthRandom(-180);
        Assert.Equal(0, DungeonGraphGenerator1458.ResolveStartY(tiles, 40, 50, 100, random));
        Assert.Equal(1, random.Draws);
    }

    private sealed class DepthRandom(int offset) : IWorldGenerationVanillaRandom
    {
        public int Draws { get; private set; }
        public int Next(int minimumInclusive, int maximumExclusive)
        { Assert.Equal(-200, minimumInclusive); Assert.Equal(200, maximumExclusive); Draws++; return offset; }
        public int Next() => throw new InvalidOperationException();
        public int Next(int maximumExclusive) => throw new InvalidOperationException();
        public double NextDouble() => throw new InvalidOperationException();
        public void NextBytes(byte[] buffer) => throw new InvalidOperationException();
    }
}
