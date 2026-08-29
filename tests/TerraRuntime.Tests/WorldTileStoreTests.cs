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
    public void Tile_mutations_mark_only_their_network_sections_dirty()
    {
        var dimensions = new WorldDimensions(401, 301);
        var store = new WorldTileStore(dimensions);

        store.Set(0, 0, default);
        store.Set(199, 149, default);
        store.Set(200, 150, default);

        Assert.Equal(2, store.DirtySections.DirtyCount);
        Assert.True(store.DirtySections.IsDirty(new WorldSectionId(0, 0)));
        Assert.True(store.DirtySections.IsDirty(new WorldSectionId(1, 1)));
        Assert.False(store.DirtySections.IsDirty(new WorldSectionId(2, 2)));
    }

    [Fact]
    public void Repeated_mutations_in_one_section_are_coalesced_until_drained()
    {
        var store = new WorldTileStore(new WorldDimensions(400, 300));

        store.Set(10, 10, default);
        store.Set(20, 20, default);
        store.Set(199, 149, default);

        Assert.Equal(1, store.DirtySections.DirtyCount);

        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, store.DirtySections.Drain(drained));
        Assert.Equal(new WorldSectionId(0, 0), drained[0]);
        Assert.Equal(0, store.DirtySections.DirtyCount);
    }

    [Fact]
    public void Runtime_tile_stays_within_sixteen_bytes()
    {
        Assert.True(Unsafe.SizeOf<WorldTile>() <= 16, $"WorldTile grew to {Unsafe.SizeOf<WorldTile>()} bytes.");
    }

    [Fact]
    public void Rejects_out_of_bounds_coordinates_without_dirtying_a_section()
    {
        var store = new WorldTileStore(new WorldDimensions(2, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => store.Get(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Set(0, -1, default));
        Assert.Equal(0, store.DirtySections.DirtyCount);
    }
}
