using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGroundFighterDoorOpeningServiceTests
{
    [Fact]
    public void Normal_door_opens_to_right_with_source_frame_style_and_row_colors()
    {
        WorldTileStore tiles = CreateWorld();
        PlaceClosedDoor(tiles, x: 10, topY: 10, frameX: 54, frameYBase: 108);
        WorldTile destination = tiles.Get(11, 11);
        destination.Wall = 7;
        destination.LiquidAmount = 81;
        destination.LiquidKind = WorldLiquidKind.Honey;
        destination.Flags = WorldTileFlags.WireBlue | WorldTileFlags.Actuator;
        tiles.Set(11, 11, in destination);

        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles);
        var intent = new VanillaGroundFighterDoorOpeningIntent(10, 11, 1, VanillaTileIds.ClosedDoor);

        Assert.True(service.TryOpen(in intent, out VanillaGroundFighterDoorOpeningMutation mutation));
        Assert.Equal(VanillaGroundFighterDoorOpeningKind.Door, mutation.Kind);
        Assert.Equal(6, mutation.ChangedTiles);
        Assert.Equal(10, mutation.PacketTileX);
        Assert.Equal(11, mutation.PacketTileY);

        for (int row = 0; row < 3; row++)
        {
            WorldTile left = tiles.Get(10, 10 + row);
            WorldTile right = tiles.Get(11, 10 + row);
            Assert.Equal(VanillaTileIds.OpenDoor, left.TileType);
            Assert.Equal(VanillaTileIds.OpenDoor, right.TileType);
            Assert.Equal((short)72, left.FrameX);
            Assert.Equal((short)90, right.FrameX);
            Assert.Equal((short)(108 + row * 18), left.FrameY);
            Assert.Equal(left.FrameY, right.FrameY);
            Assert.Equal((byte)(row + 1), left.TileColor);
            Assert.Equal(left.TileColor, right.TileColor);
            Assert.Equal(row == 1, left.IsBlockInvisible);
            Assert.Equal(row == 2, left.IsBlockFullbright);
            Assert.Equal(left.IsBlockInvisible, right.IsBlockInvisible);
            Assert.Equal(left.IsBlockFullbright, right.IsBlockFullbright);
        }

        WorldTile preserved = tiles.Get(11, 11);
        Assert.Equal((ushort)7, preserved.Wall);
        Assert.Equal((byte)81, preserved.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Honey, preserved.LiquidKind);
        Assert.True((preserved.Flags & WorldTileFlags.WireBlue) != 0);
        Assert.True((preserved.Flags & WorldTileFlags.Actuator) != 0);
    }

    [Fact]
    public void Normal_door_opens_to_left_with_vanilla_left_frame_band()
    {
        WorldTileStore tiles = CreateWorld();
        PlaceClosedDoor(tiles, 10, 10, frameX: 0, frameYBase: 0);
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles);
        var intent = new VanillaGroundFighterDoorOpeningIntent(10, 12, -1, VanillaTileIds.ClosedDoor);

        Assert.True(service.TryOpen(in intent, out _));
        for (int row = 0; row < 3; row++)
        {
            Assert.Equal(VanillaTileIds.OpenDoor, tiles.Get(9, 10 + row).TileType);
            Assert.Equal(VanillaTileIds.OpenDoor, tiles.Get(10, 10 + row).TileType);
            Assert.Equal((short)36, tiles.Get(9, 10 + row).FrameX);
            Assert.Equal((short)54, tiles.Get(10, 10 + row).FrameX);
        }
    }

    [Fact]
    public void Locked_dungeon_door_is_rejected_without_mutation()
    {
        WorldTileStore tiles = CreateWorld();
        PlaceClosedDoor(tiles, 10, 10, frameX: 0, frameYBase: 594);
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles);
        var intent = new VanillaGroundFighterDoorOpeningIntent(10, 10, 1, VanillaTileIds.ClosedDoor);
        WorldTile before = tiles.Get(10, 10);

        Assert.False(service.TryOpen(in intent, out _));
        Assert.Equal(before, tiles.Get(10, 10));
        Assert.False(tiles.Get(11, 10).IsActive);
    }

    [Fact]
    public void Non_cuttable_destination_blocks_normal_door()
    {
        WorldTileStore tiles = CreateWorld();
        PlaceClosedDoor(tiles, 10, 10, frameX: 0, frameYBase: 0);
        WorldTile stone = ActiveTile(1);
        tiles.Set(11, 11, in stone);
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles);
        var intent = new VanillaGroundFighterDoorOpeningIntent(10, 11, 1, VanillaTileIds.ClosedDoor);

        Assert.False(service.TryOpen(in intent, out _));
        Assert.Equal(VanillaTileIds.ClosedDoor, tiles.Get(10, 11).TileType);
        Assert.Equal(new TileTypeId(1), tiles.Get(11, 11).TileType);
    }

    [Fact]
    public void Cuttable_destination_is_replaced_but_independent_state_survives()
    {
        WorldTileStore tiles = CreateWorld();
        PlaceClosedDoor(tiles, 10, 10, frameX: 0, frameYBase: 0);
        WorldTile cobweb = ActiveTile(51);
        cobweb.Wall = 9;
        cobweb.LiquidAmount = 123;
        cobweb.LiquidKind = WorldLiquidKind.Lava;
        cobweb.Flags |= WorldTileFlags.WireRed | WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock;
        cobweb.TileColor = 17;
        tiles.Set(11, 11, in cobweb);
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles);
        var intent = new VanillaGroundFighterDoorOpeningIntent(10, 11, 1, VanillaTileIds.ClosedDoor);

        Assert.True(service.TryOpen(in intent, out _));
        WorldTile opened = tiles.Get(11, 11);
        Assert.Equal(VanillaTileIds.OpenDoor, opened.TileType);
        Assert.Equal((ushort)9, opened.Wall);
        Assert.Equal((byte)123, opened.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Lava, opened.LiquidKind);
        Assert.True((opened.Flags & WorldTileFlags.WireRed) != 0);
        Assert.True((opened.Flags & WorldTileFlags.Actuator) != 0);
        Assert.Equal((byte)2, opened.TileColor);
        Assert.True(opened.IsBlockInvisible);
    }

    [Fact]
    public void Tall_gate_fails_closed_without_actor_occupancy_boundary()
    {
        WorldTileStore tiles = CreateWorld();
        PlaceClosedTallGate(tiles, 15, 10);
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles);
        var intent = new VanillaGroundFighterDoorOpeningIntent(15, 12, 1, VanillaTileIds.TallGateClosed);

        Assert.False(service.TryOpen(in intent, out _));
        for (int row = 0; row < 5; row++)
            Assert.Equal(VanillaTileIds.TallGateClosed, tiles.Get(15, 10 + row).TileType);
    }

    [Fact]
    public void Tall_gate_preserves_frames_and_tile_state_when_actor_clear()
    {
        WorldTileStore tiles = CreateWorld();
        PlaceClosedTallGate(tiles, 15, 10);
        var occupancy = new FixedOccupancyProbe(actorFree: true);
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupancy);
        var intent = new VanillaGroundFighterDoorOpeningIntent(15, 13, -1, VanillaTileIds.TallGateClosed);

        Assert.True(service.TryOpen(in intent, out VanillaGroundFighterDoorOpeningMutation mutation));
        Assert.Equal(VanillaGroundFighterDoorOpeningKind.TallGate, mutation.Kind);
        Assert.Equal(5, mutation.ChangedTiles);
        Assert.Equal(5, occupancy.Calls);
        for (int row = 0; row < 5; row++)
        {
            WorldTile gate = tiles.Get(15, 10 + row);
            Assert.Equal(VanillaTileIds.TallGateOpen, gate.TileType);
            Assert.Equal((short)(row * 18), gate.FrameY);
            Assert.Equal((byte)(20 + row), gate.TileColor);
            Assert.True((gate.Flags & WorldTileFlags.WireGreen) != 0);
        }
    }

    [Fact]
    public void Tall_gate_actor_collision_rejects_entire_object_atomically()
    {
        WorldTileStore tiles = CreateWorld();
        PlaceClosedTallGate(tiles, 15, 10);
        var occupancy = new SelectiveOccupancyProbe(blockedY: 12);
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupancy);
        var intent = new VanillaGroundFighterDoorOpeningIntent(15, 10, 1, VanillaTileIds.TallGateClosed);

        Assert.False(service.TryOpen(in intent, out _));
        for (int row = 0; row < 5; row++)
            Assert.Equal(VanillaTileIds.TallGateClosed, tiles.Get(15, 10 + row).TileType);
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(40, 40));

    private static void PlaceClosedDoor(
        WorldTileStore tiles,
        int x,
        int topY,
        short frameX,
        short frameYBase)
    {
        for (int row = 0; row < 3; row++)
        {
            WorldTile tile = ActiveTile(checked((ushort)VanillaTileIds.ClosedDoor.Value));
            tile.FrameX = frameX;
            tile.FrameY = checked((short)(frameYBase + row * 18));
            tile.TileColor = checked((byte)(row + 1));
            if (row == 1)
                tile.Flags |= WorldTileFlags.InvisibleBlock;
            if (row == 2)
                tile.Flags |= WorldTileFlags.FullbrightBlock;
            tiles.Set(x, topY + row, in tile);
        }
    }

    private static void PlaceClosedTallGate(WorldTileStore tiles, int x, int topY)
    {
        for (int row = 0; row < 5; row++)
        {
            WorldTile tile = ActiveTile(checked((ushort)VanillaTileIds.TallGateClosed.Value));
            tile.FrameX = 0;
            tile.FrameY = checked((short)(row * 18));
            tile.TileColor = checked((byte)(20 + row));
            tile.Flags |= WorldTileFlags.WireGreen;
            tiles.Set(x, topY + row, in tile);
        }
    }

    private static WorldTile ActiveTile(ushort type) => new()
    {
        Type = type,
        Flags = WorldTileFlags.Active
    };

    private sealed class FixedOccupancyProbe(bool actorFree) : IVanillaTallGateOccupancyProbe
    {
        public int Calls { get; private set; }

        public bool IsActorFree(int tileX, int tileY)
        {
            Calls++;
            return actorFree;
        }
    }

    private sealed class SelectiveOccupancyProbe(int blockedY) : IVanillaTallGateOccupancyProbe
    {
        public bool IsActorFree(int tileX, int tileY) => tileY != blockedY;
    }
}
