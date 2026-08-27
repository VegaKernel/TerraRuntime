using System.Runtime.CompilerServices;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldTileStoreTests
{
    [Fact]
    public void Stores_tiles_without_crossing_coordinates()
    {
        var dimensions = new WorldDimensions(3, 2);
        var store = new WorldTileStore(dimensions);
        var tile = new WorldTile
        {
            Type = 42,
            Wall = 7,
            FrameX = 18,
            FrameY = 36,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
            LiquidAmount = 200,
            LiquidKind = WorldLiquidKind.Shimmer,
            TileColor = 3,
            WallColor = 4,
            Shape = 2
        };

        store.Set(2, 1, tile);

        Assert.Equal(6, store.Count);
        Assert.Equal((ushort)42, store.Get(2, 1).Type);
        Assert.Equal(WorldLiquidKind.Shimmer, store.Get(2, 1).LiquidKind);
        Assert.Equal((ushort)0, store.Get(1, 1).Type);
    }

    [Fact]
    public void Runtime_tile_stays_within_sixteen_bytes()
    {
        Assert.True(Unsafe.SizeOf<WorldTile>() <= 16, $"WorldTile grew to {Unsafe.SizeOf<WorldTile>()} bytes.");
    }

    [Fact]
    public void Rejects_out_of_bounds_coordinates()
    {
        var store = new WorldTileStore(new WorldDimensions(2, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => store.Get(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Set(0, -1, default));
    }
}
