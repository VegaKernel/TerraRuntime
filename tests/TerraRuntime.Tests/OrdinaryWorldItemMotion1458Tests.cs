using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OrdinaryWorldItemMotion1458Tests
{
    [Theory]
    [InlineData(false, false, 2.1f, 161.9f, 162.1f)]
    [InlineData(true, false, 2.08f, 161f, 161f)]
    [InlineData(true, true, 2.05f, 160.5f, 160.5f)]
    public void Gravity_and_pre_gravity_wet_displacement_match_source(bool wasWet, bool wasHoney, float expectedVy, float expectedX, float expectedY)
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 200));
        if (wasWet)
            for (int x = 9; x < 14; x++)
            for (int y = 9; y < 14; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { LiquidAmount = 255, LiquidKind = wasHoney ? WorldLiquidKind.Honey : WorldLiquidKind.Water };
        Assert.True(VanillaOrdinaryWorldItemMotion1458.TryStep(tiles, 14, 26, 160, 160, 2, 2, wasWet, wasHoney, false,
            out float nx, out float ny, out float vx, out float vy, out bool wet, out _, out _));
        Assert.Equal(expectedX, nx); Assert.Equal(expectedY, ny);
        Assert.Equal(1.9f, vx); Assert.Equal(expectedVy, vy); Assert.Equal(wasWet, wet);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(421, 0)]
    [InlineData(422, 0)]
    [InlineData(19, 0)]
    public void Unrepresented_slope_conveyor_and_platform_contexts_fail_closed(ushort type, byte shape)
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 200));
        tiles.Tiles[tiles.GetUncheckedIndex(10, 12)] = new WorldTile { Type = type, Shape = shape, Flags = WorldTileFlags.Active };
        Assert.False(VanillaOrdinaryWorldItemMotion1458.TryStep(tiles, 14, 26, 160, 160, 2, 2, false, false, false,
            out _, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void Flat_floor_stops_motion_without_drifting_through_tiles()
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 200));
        for (int x = 8; x < 16; x++) tiles.Tiles[tiles.GetUncheckedIndex(x, 12)] = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        Assert.True(VanillaOrdinaryWorldItemMotion1458.TryStep(tiles, 14, 26, 160, 166, 0, 6, false, false, false,
            out float nx, out float ny, out _, out float vy, out _, out _, out _));
        Assert.Equal(160, nx); Assert.Equal(166, ny); Assert.Equal(0, vy);
    }

    [Fact]
    public void Silent_motion_rejects_stale_generation_and_preserves_item_fields()
    {
        var store = new RuntimeWorldItemStore();
        var drop = new WorldItemDropStateUpdate(160, 160, 1, 2, 4, 0, WorldItemOwnershipMode.None, 267, false, 0, 9);
        Assert.True(store.TryAllocateDrop(in drop, out var old));
        Assert.True(store.TryRemove(old.Handle.Slot, out _));
        Assert.True(store.TryAllocateDrop(in drop, out var current));
        Assert.False(store.TryAdvanceMotion(old.Handle, 500, 500, 0, 0, out _));
        Assert.True(store.TryAdvanceMotion(current.Handle, 161, 162, .95f, 2.1f, out var advanced));
        Assert.Equal(4, advanced.Stack); Assert.Equal(267, advanced.ItemNetId);
        Assert.Equal(9, advanced.EnemyGrabDelayTime); Assert.Equal(255, advanced.OwnerPlayerId);
        Assert.True(advanced.Revision.Value > current.Revision.Value);
        store.TickReservationTimers();
        Assert.True(store.TryGetActive(current.Handle.Slot, out var ticked));
        Assert.Equal(8, ticked.EnemyGrabDelayTime);
    }
}
