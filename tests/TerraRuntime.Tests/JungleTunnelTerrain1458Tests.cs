using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class JungleTunnelTerrain1458Tests
{
    // Official 1.4.5.8 KillTile, generation/loading flags true, dedicated netMode2,
    // natural tile at40,40, wall64 and liquid80. Same pinned binary as TerrainReference1458Tests.
    [Theory]
    [InlineData(0, 906992634)]
    [InlineData(1, 906992634)]
    [InlineData(2, 955664633)]
    [InlineData(40, 906992634)]
    [InlineData(53, 906992634)]
    [InlineData(59, 906992634)]
    [InlineData(63, 906992634)]
    [InlineData(64, 906992634)]
    [InlineData(65, 906992634)]
    [InlineData(66, 906992634)]
    [InlineData(67, 906992634)]
    [InlineData(68, 906992634)]
    [InlineData(147, 906992634)]
    [InlineData(161, 906992634)]
    public void Natural_generation_kill_matches_source_metadata_and_rng(ushort type, int next)
    {
        var random = new RandomAdapter();
        WorldTile tile = new()
        {
            Type = type, Wall = 64, LiquidAmount = 80, LiquidKind = WorldLiquidKind.Lava,
            FrameX = 18, FrameY = 36, Shape = 3, TileColor = 5, WallColor = 6,
            Flags = WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock |
                WorldTileFlags.FullbrightBlock | WorldTileFlags.WireRed | WorldTileFlags.Actuator |
                WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall
        };
        WorldTile expected = tile;
        expected.Type = 0;
        expected.FrameX = expected.FrameY = -1;
        expected.Shape = expected.TileColor = 0;
        expected.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive |
            WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);

        EarlyPass1458.ClearJungleTunnelTerrain(ref tile, random);

        Assert.Equal(expected, tile);
        Assert.Equal(next, random.Next());
    }

    [Fact]
    public void Inactive_cell_is_not_killed_or_rerolled()
    {
        WorldTile tile = new() { Type = 59, FrameX = 18, FrameY = 36, LiquidAmount = 80 };
        WorldTile expected = tile;
        var random = new RandomAdapter();
        EarlyPass1458.ClearJungleTunnelTerrain(ref tile, random);
        Assert.Equal(expected, tile);
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Unknown_object_semantics_abort_without_mutation_or_rng()
    {
        WorldTile tile = new() { Type = 21, Flags = WorldTileFlags.Active, FrameX = 18, Wall = 64 };
        WorldTile expected = tile;
        var random = new RandomAdapter();
        Assert.Throws<InvalidOperationException>(() => EarlyPass1458.ClearJungleTunnelTerrain(ref tile, random));
        Assert.Equal(expected, tile);
        Assert.Equal(906992634, random.Next());
    }

    private sealed class RandomAdapter : EarlyPass1458.IRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(1458);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
    }
}
