using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldTileContentIdTests
{
    [Fact]
    public void Typed_content_accessors_preserve_the_packed_world_tile_abi()
    {
        Assert.Equal(16, Marshal.SizeOf<WorldTile>());

        var tile = new WorldTile
        {
            Type = 321,
            Wall = 45
        };

        Assert.Equal(new TileTypeId(321), tile.TileType);
        Assert.Equal(new WallTypeId(45), tile.WallType);
        Assert.Equal((ushort)321, tile.Type);
        Assert.Equal((ushort)45, tile.Wall);
    }

    [Fact]
    public void Typed_setters_update_only_the_existing_storage_fields()
    {
        var tile = new WorldTile
        {
            FrameX = 18,
            FrameY = 36,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
            LiquidAmount = 100
        };

        Assert.True(tile.TrySetTileType(new TileTypeId(777)));
        Assert.True(tile.TrySetWallType(new WallTypeId(88)));

        Assert.Equal((ushort)777, tile.Type);
        Assert.Equal((ushort)88, tile.Wall);
        Assert.Equal((short)18, tile.FrameX);
        Assert.Equal((short)36, tile.FrameY);
        Assert.Equal(WorldTileFlags.Active | WorldTileFlags.WireRed, tile.Flags);
        Assert.Equal((byte)100, tile.LiquidAmount);
    }

    [Fact]
    public void Typed_setters_reject_ids_that_do_not_fit_the_snapshot_abi()
    {
        var tile = new WorldTile
        {
            Type = 7,
            Wall = 9
        };

        Assert.False(tile.TrySetTileType(new TileTypeId(ushort.MaxValue + 1)));
        Assert.False(tile.TrySetWallType(new WallTypeId(ushort.MaxValue + 1)));
        Assert.Equal((ushort)7, tile.Type);
        Assert.Equal((ushort)9, tile.Wall);
    }
}
