using System.Runtime.InteropServices;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldTileStateFlagsTests
{
    [Fact]
    public void Named_flags_pin_the_packed_snapshot_abi()
    {
        Assert.Equal(16, Marshal.SizeOf<WorldTile>());
        Assert.Equal((WorldTileFlags)0x0001, WorldTileFlags.Active);
        Assert.Equal((WorldTileFlags)0x0002, WorldTileFlags.WireRed);
        Assert.Equal((WorldTileFlags)0x0004, WorldTileFlags.WireBlue);
        Assert.Equal((WorldTileFlags)0x0008, WorldTileFlags.WireGreen);
        Assert.Equal((WorldTileFlags)0x0010, WorldTileFlags.WireYellow);
        Assert.Equal((WorldTileFlags)0x0020, WorldTileFlags.Actuator);
        Assert.Equal((WorldTileFlags)0x0040, WorldTileFlags.Inactive);
        Assert.Equal((WorldTileFlags)0x0080, WorldTileFlags.InvisibleBlock);
        Assert.Equal((WorldTileFlags)0x0100, WorldTileFlags.InvisibleWall);
        Assert.Equal((WorldTileFlags)0x0200, WorldTileFlags.FullbrightBlock);
        Assert.Equal((WorldTileFlags)0x0400, WorldTileFlags.FullbrightWall);
        Assert.Equal((WorldTileFlags)0x07FF, WorldTileFlagMasks.Known);
    }

    [Fact]
    public void Flag_groups_have_named_non_overlapping_ownership()
    {
        Assert.Equal(
            WorldTileFlags.WireRed |
            WorldTileFlags.WireBlue |
            WorldTileFlags.WireGreen |
            WorldTileFlags.WireYellow,
            WorldTileFlagMasks.Wires);

        Assert.Equal(WorldTileFlags.None, WorldTileFlagMasks.Wires & WorldTileFlagMasks.Actuation);
        Assert.Equal(WorldTileFlags.None, WorldTileFlagMasks.Wires & WorldTileFlagMasks.Visibility);
        Assert.Equal(WorldTileFlags.None, WorldTileFlagMasks.Wires & WorldTileFlagMasks.Fullbright);
        Assert.Equal(WorldTileFlags.None, WorldTileFlagMasks.Actuation & WorldTileFlagMasks.Visibility);
        Assert.Equal(WorldTileFlags.None, WorldTileFlagMasks.Visibility & WorldTileFlagMasks.Fullbright);
    }

    [Fact]
    public void Semantic_accessors_read_named_runtime_state()
    {
        var tile = new WorldTile
        {
            Flags =
                WorldTileFlags.Active |
                WorldTileFlags.WireBlue |
                WorldTileFlags.Actuator |
                WorldTileFlags.Inactive |
                WorldTileFlags.InvisibleBlock |
                WorldTileFlags.FullbrightWall
        };

        Assert.True(tile.IsActive);
        Assert.True(tile.HasAnyWire);
        Assert.True(tile.HasActuator);
        Assert.True(tile.IsActuated);
        Assert.True(tile.IsBlockInvisible);
        Assert.False(tile.IsWallInvisible);
        Assert.False(tile.IsBlockFullbright);
        Assert.True(tile.IsWallFullbright);
        Assert.True(tile.HasOnlyKnownFlags);
    }

    [Fact]
    public void TrySetFlags_preserves_unrelated_state_and_rejects_unknown_bits()
    {
        var tile = new WorldTile
        {
            Type = 123,
            Wall = 45,
            FrameX = 18,
            FrameY = 36,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
            LiquidAmount = 80
        };

        Assert.True(tile.TrySetFlags(WorldTileFlags.WireBlue | WorldTileFlags.Actuator, enabled: true));
        Assert.True(tile.TrySetFlags(WorldTileFlags.WireRed, enabled: false));

        Assert.Equal(
            WorldTileFlags.Active | WorldTileFlags.WireBlue | WorldTileFlags.Actuator,
            tile.Flags);
        Assert.Equal((ushort)123, tile.Type);
        Assert.Equal((ushort)45, tile.Wall);
        Assert.Equal((short)18, tile.FrameX);
        Assert.Equal((short)36, tile.FrameY);
        Assert.Equal((byte)80, tile.LiquidAmount);

        WorldTileFlags before = tile.Flags;
        Assert.False(tile.TrySetFlags((WorldTileFlags)0x8000, enabled: true));
        Assert.Equal(before, tile.Flags);

        tile.Flags |= (WorldTileFlags)0x8000;
        Assert.False(tile.HasOnlyKnownFlags);
    }
}
