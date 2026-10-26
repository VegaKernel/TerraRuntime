using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldWiringDeviceExecutor1458Tests
{
    [Fact]
    public void Source_single_cell_light_frame_steps_preserve_independent_state_and_publish_after_commit()
    {
        // Literal Wiring1458 ToggleTorch/ToggleCandle/ToggleHolidayLight frame thresholds.
        (ushort Type, short Before, short After)[] rows =
        [ (4, 0, 66), (4, 22, 88), (4, 66, 0), (33, 0, 18), (49, 18, 0),
          (174, 0, 18), (372, 18, 0), (646, 0, 18), (149, 36, 90), (149, 90, 36) ];
        foreach (var row in rows)
        {
            var tiles = Tiles();
            var before = Cell(row.Type, row.Before, 108);
            before.Wall = 17;
            before.TileColor = 9;
            before.LiquidAmount = 55;
            before.LiquidKind = WorldLiquidKind.Honey;
            before.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
            tiles.Set(20, 20, in before);
            tiles.DirtySections.Clear();
            tiles.PersistenceDirtySections.Clear();
            int published = 0, skipped = 0;
            var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (x, y, w, h) =>
            {
                Assert.Equal((20, 20, (byte)1, (byte)1), (x, y, w, h));
                Assert.Equal(row.After, tiles.Get(x, y).FrameX);
                Assert.Equal(1, tiles.DirtySections.DirtyCount);
                Assert.Equal(1, tiles.PersistenceDirtySections.DirtyCount);
                published++;
            });
            Assert.True(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => skipped++));
            var expected = before;
            expected.FrameX = row.After;
            Assert.Equal(expected, tiles.Get(20, 20));
            Assert.Equal(1, published);
            Assert.Equal(0, skipped); // Source single-cell light branches do not call SkipWire.
        }
    }

    [Fact]
    public void Source_multicell_lights_resolve_nonanchor_frames_and_skip_each_cell_once()
    {
        // Anchor/touched frames are literal source-derived light transformations;
        // dimensions and frame strides are not obtained from the implementation under test.
        (ushort Type, int Width, int Height, int TouchX, int TouchY, short AnchorX, short AnchorY,
            short DeltaX, short DeltaY)[] rows =
        [ (42, 1, 2, 0, 1, 0, 72, 18, 0), (93, 1, 3, 0, 2, 0, 108, 18, 0),
          (92, 1, 6, 0, 5, 0, 0, 18, 0), (95, 2, 2, 1, 1, 0, 72, 36, 0),
          (100, 2, 2, 1, 0, 36, 36, -36, 0), (126, 2, 2, 0, 1, 0, 0, 36, 0),
          (173, 2, 2, 1, 1, 0, 0, 36, 0), (564, 2, 2, 1, 1, 36, 0, -36, 0),
          (34, 3, 3, 2, 2, 108, 108, 54, 0), (34, 3, 3, 1, 2, 162, 54, -54, 0),
          (215, 3, 2, 2, 1, 108, 0, 0, 36), (215, 3, 2, 1, 1, 54, 36, 0, -36),
          (405, 3, 2, 2, 1, 0, 0, 54, 0), (411, 2, 2, 1, 1, 0, 0, 36, 0),
          (411, 2, 2, 1, 0, 36, 0, -36, 0) ];
        foreach (var row in rows)
        {
            var tiles = Tiles();
            for (int col = 0; col < row.Width; col++)
            for (int line = 0; line < row.Height; line++)
                tiles.Set(20 + col, 20 + line, Cell(row.Type,
                    (short)(row.AnchorX + 18 * col), (short)(row.AnchorY + 18 * line)));
            var skips = new List<(int, int)>();
            var boxes = new List<(int, int, byte, byte)>();
            var devices = new WorldWiringDeviceExecutor1458(tiles,
                publishTileSquare: (x, y, w, h) => boxes.Add((x, y, w, h)));
            Assert.True(devices.Hit(20 + row.TouchX, 20 + row.TouchY, WorldWireChannel.Yellow,
                (x, y) => skips.Add((x, y))));
            Assert.Equal(row.Width * row.Height, skips.Count);
            Assert.Equal(skips.Count, skips.Distinct().Count());
            Assert.Single(boxes);
            Assert.Equal((20, 20, (byte)row.Width, (byte)row.Height), boxes[0]);
            for (int col = 0; col < row.Width; col++)
            for (int line = 0; line < row.Height; line++)
            {
                Assert.Contains((20 + col, 20 + line), skips);
                var cell = tiles.Get(20 + col, 20 + line);
                Assert.Equal(row.AnchorX + 18 * col + row.DeltaX, cell.FrameX);
                Assert.Equal(row.AnchorY + 18 * line + row.DeltaY, cell.FrameY);
            }
            Assert.True(devices.Hit(20 + row.TouchX, 20 + row.TouchY, WorldWireChannel.Yellow, (_, _) => { }));
            for (int col = 0; col < row.Width; col++)
            for (int line = 0; line < row.Height; line++)
            {
                Assert.Equal(row.AnchorX + 18 * col, tiles.Get(20 + col, 20 + line).FrameX);
                Assert.Equal(row.AnchorY + 18 * line, tiles.Get(20 + col, 20 + line).FrameY);
            }
        }
    }

    [Fact]
    public void Actuator_respects_source_Plantera_boundary_and_updates_the_existing_collision_store()
    {
        var tiles = Tiles();
        var cell = Cell(226, 0, 0);
        cell.Flags |= WorldTileFlags.Actuator;
        tiles.Set(20, 20, in cell);
        bool plantBossDowned = false;
        var devices = new WorldWiringDeviceExecutor1458(tiles,
            plantBossDowned: () => plantBossDowned, worldSurface: 19);
        Assert.True(VanillaWorldSolidCollision.Intersects(tiles, 320, 320, 16, 16));
        Assert.False(devices.Hit(20, 20, WorldWireChannel.Green, (_, _) => { }));
        Assert.False(tiles.Get(20, 20).IsActuated);
        plantBossDowned = true;
        Assert.True(devices.Hit(20, 20, WorldWireChannel.Green, (_, _) => { }));
        Assert.True(tiles.Get(20, 20).IsActive);
        Assert.True(tiles.Get(20, 20).HasActuator);
        Assert.True(tiles.Get(20, 20).IsActuated);
        Assert.False(VanillaWorldSolidCollision.Intersects(tiles, 320, 320, 16, 16));
        // Reactivation has no progression or tile-above guard, even if progression changes later.
        plantBossDowned = false;
        tiles.Set(20, 19, Cell(21, 0, 0));
        Assert.True(devices.Hit(20, 20, WorldWireChannel.Green, (_, _) => { }));
        Assert.True(VanillaWorldSolidCollision.Intersects(tiles, 320, 320, 16, 16));
        // j == worldSurface is allowed by the source's strictly-greater comparison.
        tiles.Set(20, 19, default);
        var boundary = new WorldWiringDeviceExecutor1458(tiles, worldSurface: 20);
        Assert.True(boundary.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
    }

    [Fact]
    public void Actuator_under_objects_calls_existing_CanKill_policy_only_after_source_support_guards()
    {
        var tiles = Tiles();
        var stone = Cell(1, 0, 0);
        stone.Flags |= WorldTileFlags.Actuator;
        int calls = 0;
        bool permitted = false;
        var devices = new WorldWiringDeviceExecutor1458(tiles, canKillTile: (x, y) =>
        {
            Assert.Equal((20, 20), (x, y));
            calls++;
            return permitted;
        });
        foreach (ushort type in new ushort[] { 21, 467, 26, 77, 88, 470, 475, 237, 597, 441, 468 })
        {
            tiles.Set(20, 20, in stone);
            tiles.Set(20, 19, Cell(type, 0, 0));
            Assert.False(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
            Assert.False(tiles.Get(20, 20).IsActuated);
        }
        Assert.Equal(0, calls);
        tiles.Set(20, 19, Cell(5, 0, 0));
        Assert.False(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
        Assert.Equal(1, calls);
        permitted = true;
        Assert.True(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
        Assert.Equal(2, calls);
        tiles.Set(20, 20, in stone);
        tiles.Set(20, 19, default);
        Assert.True(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
        Assert.Equal(2, calls); // Source !above.active short-circuits CanKillTile.
    }

    [Fact]
    public void Actuators_reject_source_nonsolid_and_explicit_exclusions_without_fabricating_changes()
    {
        var tiles = Tiles();
        var devices = new WorldWiringDeviceExecutor1458(tiles, canKillTile: (_, _) => true);
        // Door identities have separate device effects; this test isolates their actuator state only.
        foreach (ushort type in new ushort[] { 3, 10, 314, 379, 386, 387, 388, 389, 476 })
        {
            var cell = Cell(type, 0, 0);
            cell.Flags |= WorldTileFlags.Actuator;
            tiles.Set(20, 20, in cell);
            Assert.False(devices.Hit(20, 20, WorldWireChannel.Blue, (_, _) => { }));
            Assert.Equal(cell, tiles.Get(20, 20));
        }
    }

    [Fact]
    public void Wire_doors_use_shared_mutation_RNG_and_source_specific_skipped_footprints()
    {
        var tiles = Tiles();
        for (int row = 0; row < 3; row++) tiles.Set(20, 20 + row, Cell(10, 0, (short)(18 * row)));
        var frameRandom = new DoorRandom();
        var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, new Occupancy(true), frameRandom);
        int directions = 0, published = 0;
        var skipped = new List<(int, int)>();
        var devices = new WorldWiringDeviceExecutor1458(tiles, doors,
            publishTileSquare: (_, _, _, _) => published++, nextDoorDirection: () => { directions++; return 1; });
        Assert.True(devices.Hit(20, 21, WorldWireChannel.Red, (x, y) => skipped.Add((x, y))));
        Assert.Equal(1, directions);
        Assert.Equal(0, frameRandom.Calls);
        Assert.Equal((ushort)11, tiles.Get(20, 20).Type);
        Assert.Equal((ushort)11, tiles.Get(21, 22).Type);
        Assert.Equal(new[] { (20, 20), (20, 21), (20, 22), (21, 20), (21, 21), (21, 22) }, skipped);
        skipped.Clear();
        Assert.True(devices.Hit(20, 21, WorldWireChannel.Red, (x, y) => skipped.Add((x, y))));
        Assert.Equal(3, frameRandom.Calls);
        Assert.Equal((ushort)10, tiles.Get(20, 20).Type);
        Assert.False(tiles.Get(21, 22).IsActive);
        Assert.Equal(new[] { (20, 20), (20, 21), (20, 22) }, skipped);
        Assert.Equal(2, published);
    }

    [Fact]
    public void Real_circuit_multiwired_door_operates_once_per_color_and_again_on_the_next_press()
    {
        var tiles = Tiles();
        PutWireSource(tiles);
        for (int row = 0; row < 3; row++) tiles.Set(20, 20 + row, RedCell(10, 0, (short)(18 * row)));
        var frameRandom = new DoorRandom();
        int directions = 0, published = 0;
        var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, new Occupancy(true), frameRandom);
        var devices = new WorldWiringDeviceExecutor1458(tiles, doors,
            publishTileSquare: (_, _, _, _) => published++, nextDoorDirection: () => { directions++; return 1; });
        var circuit = new WorldWiringCircuit1458(tiles, devices);
        Assert.True(circuit.HitSwitch(18, 20));
        Assert.Equal(1, directions);
        Assert.Equal(0, frameRandom.Calls);
        Assert.Equal(1, published);
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 2; col++) Assert.Equal((ushort)11, tiles.Get(20 + col, 20 + row).Type);
        Assert.True(circuit.HitSwitch(18, 20));
        Assert.Equal(1, directions);
        Assert.Equal(3, frameRandom.Calls);
        Assert.Equal(2, published);
        for (int row = 0; row < 3; row++)
        {
            Assert.Equal((ushort)10, tiles.Get(20, 20 + row).Type);
            Assert.False(tiles.Get(21, 20 + row).IsActive);
        }
    }

    [Fact]
    public void Real_circuit_multiwired_trapdoor_skips_the_admitted_source_and_destination_rows()
    {
        var tiles = Tiles();
        PutWireSource(tiles);
        for (int col = 0; col < 2; col++)
        {
            tiles.Set(20 + col, 20, RedCell(387, (short)(18 * col), 0));
            tiles.Set(20 + col, 21, new WorldTile { Flags = WorldTileFlags.WireRed });
        }
        int published = 0;
        var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, new Occupancy(true));
        var devices = new WorldWiringDeviceExecutor1458(tiles, doors,
            publishTileSquare: (_, _, _, _) => published++);
        var circuit = new WorldWiringCircuit1458(tiles, devices);
        Assert.True(circuit.HitSwitch(18, 20));
        Assert.Equal(1, published);
        for (int col = 0; col < 2; col++)
        for (int row = 0; row < 2; row++)
        {
            var cell = tiles.Get(20 + col, 20 + row);
            Assert.Equal((ushort)386, cell.Type);
            Assert.Equal(36 + col * 18, cell.FrameX);
            Assert.Equal(row * 18, cell.FrameY);
        }
        Assert.True(circuit.HitSwitch(18, 20));
        Assert.Equal(2, published);
        for (int col = 0; col < 2; col++)
        {
            Assert.Equal((ushort)387, tiles.Get(20 + col, 20).Type);
            Assert.False(tiles.Get(20 + col, 21).IsActive);
        }
    }

    [Fact]
    public void Real_circuit_multiwired_five_row_gate_skips_even_when_actor_occupancy_rejects_it()
    {
        var tiles = Tiles();
        PutWireSource(tiles);
        short[] frameY = [0, 20, 38, 56, 74];
        for (int row = 0; row < 5; row++) tiles.Set(20, 20 + row, RedCell(388, 0, frameY[row]));
        var occupancy = new Occupancy(false);
        int published = 0;
        var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupancy);
        var devices = new WorldWiringDeviceExecutor1458(tiles, doors,
            publishTileSquare: (_, _, _, _) => published++);
        var circuit = new WorldWiringCircuit1458(tiles, devices);
        Assert.True(circuit.HitSwitch(18, 20));
        Assert.Equal(1, occupancy.Calls); // One rejected operation, not one attempt per wired row.
        Assert.Equal(1, published); // The rejected source gate action is followed by an authoritative image.
        for (int row = 0; row < 5; row++) Assert.Equal((ushort)388, tiles.Get(20, 20 + row).Type);
        occupancy.Free = true;
        occupancy.Calls = 0;
        Assert.True(circuit.HitSwitch(18, 20));
        Assert.Equal(5, occupancy.Calls);
        Assert.Equal(2, published);
        for (int row = 0; row < 5; row++) Assert.Equal((ushort)389, tiles.Get(20, 20 + row).Type);
        Assert.True(circuit.HitSwitch(18, 20));
        Assert.Equal(10, occupancy.Calls);
        Assert.Equal(3, published);
        for (int row = 0; row < 5; row++) Assert.Equal((ushort)388, tiles.Get(20, 20 + row).Type);
    }

    [Fact]
    public void Trapdoor_style_one_closing_skips_source_coordinates_before_actor_rejection()
    {
        var tiles = Tiles();
        for (int col = 0; col < 2; col++)
        for (int row = 0; row < 2; row++) tiles.Set(20 + col, 20 + row,
            RedCell(386, (short)(36 + col * 18), (short)(18 * row)));
        var occupancy = new Occupancy(false);
        var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupancy);
        var skips = new List<(int, int)>();
        Assert.False(doors.TryShiftTrapdoor(21, 21, playerAbove: true, opening: false, out _,
            (x, y) => { Assert.Equal(0, occupancy.Calls); skips.Add((x, y)); }));
        Assert.Equal(new[] { (20, 19), (20, 20), (21, 19), (21, 20) }, skips);
        Assert.Equal(1, occupancy.Calls);
        for (int col = 0; col < 2; col++)
        for (int row = 0; row < 2; row++) Assert.Equal((ushort)386, tiles.Get(20 + col, 20 + row).Type);
    }

    [Fact]
    public void Door_actions_precede_authoritative_images_with_source_actions_and_actual_directions()
    {
        foreach (ushort type in new ushort[] { 10, 387, 388 })
        {
            var tiles = Tiles();
            if (type == 10)
                for (int row = 0; row < 3; row++) tiles.Set(20, 20 + row, RedCell(type, 0, (short)(row * 18)));
            else if (type == 387)
                for (int col = 0; col < 2; col++) tiles.Set(20 + col, 20, RedCell(type, (short)(col * 18), 0));
            else
            {
                short[] frames = [0, 20, 38, 56, 74];
                for (int row = 0; row < 5; row++) tiles.Set(20, 20 + row, RedCell(type, 0, frames[row]));
            }
            var events = new List<(int Packet, int Action, int Direction)>();
            var occupancy = new Occupancy(true);
            var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupancy, new DoorRandom());
            var devices = new WorldWiringDeviceExecutor1458(tiles, doors,
                publishTileSquare: (_, _, _, _) => events.Add((20, -1, 0)), nextDoorDirection: () => 1,
                publishDoorToggle: transition =>
                {
                    Assert.Equal((20, 20), (transition.X, transition.Y));
                    events.Add((19, (byte)transition.Action, transition.Direction));
                });
            Assert.True(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
            Assert.True(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
            int openAction = type == 10 ? 0 : type == 387 ? 2 : 4;
            int openDirection = type == 388 ? -1 : 1;
            int closeDirection = type == 387 ? 1 : -1;
            Assert.Equal(new[] { (19, openAction, openDirection), (20, -1, 0),
                (19, openAction + 1, closeDirection), (20, -1, 0) }, events);
            if (type != 388) continue;
            events.Clear();
            occupancy.Free = false;
            Assert.False(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
            Assert.Equal(new[] { (19, 4, -1), (20, -1, 0) }, events);
            Assert.Equal((ushort)388, tiles.Get(20, 20).Type);
        }

        // Both ordinary doors and closed trapdoors report the actual successful fallback direction.
        foreach (ushort type in new ushort[] { 10, 387 })
        {
            var tiles = Tiles();
            if (type == 10)
            {
                for (int row = 0; row < 3; row++)
                {
                    tiles.Set(20, 20 + row, RedCell(type, 0, (short)(18 * row)));
                    tiles.Set(21, 20 + row, RedCell(1, 0, 0));
                }
            }
            else for (int col = 0; col < 2; col++)
            {
                tiles.Set(20 + col, 20, RedCell(type, (short)(18 * col), 0));
                tiles.Set(20 + col, 21, RedCell(1, 0, 0));
            }
            var events = new List<int>();
            WorldWiringDoorTransition1458 transition = default;
            var devices = new WorldWiringDeviceExecutor1458(tiles,
                new VanillaWorldGroundFighterDoorOpeningService(tiles, new Occupancy(true)),
                publishTileSquare: (_, _, _, _) => events.Add(20), nextDoorDirection: () => 1,
                publishDoorToggle: value => { transition = value; events.Add(19); });
            Assert.True(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => { }));
            Assert.Equal(new[] { 19, 20 }, events);
            Assert.Equal(new WorldWiringDoorTransition1458(type == 10 ? WorldWiringDoorAction1458.OpenDoor :
                WorldWiringDoorAction1458.OpenTrapdoor, 20, 20, -1), transition);
        }
    }

    [Fact]
    public void Trapdoor_close_uses_source_ClearTile_and_retains_inactive_type_frames_paint_and_coating()
    {
        var tiles = Tiles();
        for (int col = 0; col < 2; col++)
        for (int row = 0; row < 2; row++)
        {
            var cell = RedCell(386, (short)(36 + 18 * col), (short)(18 * row));
            cell.TileColor = (byte)(row + 5);
            cell.Wall = 17;
            cell.LiquidAmount = 23;
            cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
            Assert.True(cell.TrySetSourceShape(halfBrick: true, slope: 2));
            tiles.Set(20 + col, 20 + row, in cell);
        }
        var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, new Occupancy(true));
        Assert.True(doors.TryShiftTrapdoor(20, 20, playerAbove: true, opening: false, out _));
        for (int col = 0; col < 2; col++)
        {
            var retained = tiles.Get(20 + col, 20);
            Assert.Equal((byte)5, retained.TileColor);
            Assert.Equal((ushort)387, retained.Type);
            var cleared = tiles.Get(20 + col, 21);
            Assert.False(cleared.IsActive);
            Assert.False(cleared.IsActuated);
            Assert.Equal((ushort)386, cleared.Type);
            Assert.Equal(36 + 18 * col, cleared.FrameX);
            Assert.Equal((short)18, cleared.FrameY);
            Assert.Equal((byte)6, cleared.TileColor);
            Assert.True(cleared.IsBlockInvisible);
            Assert.True(cleared.IsBlockFullbright);
            Assert.Equal((ushort)17, cleared.Wall);
            Assert.Equal((byte)23, cleared.LiquidAmount);
            Assert.Equal(WorldTileFlags.WireRed, cleared.Flags & WorldTileFlagMasks.Wires);
            Assert.False(cleared.IsHalfBrick);
            Assert.Equal((byte)0, cleared.SourceSlope);
        }
    }

    [Fact]
    public void Tall_gate_wire_path_checks_source_actor_occupancy_for_all_five_rows()
    {
        var tiles = Tiles();
        short[] frameY = [0, 20, 38, 56, 74];
        for (int row = 0; row < 5; row++) tiles.Set(20, 20 + row, Cell(388, 0, frameY[row]));
        var occupancy = new Occupancy(false);
        var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupancy);
        var devices = new WorldWiringDeviceExecutor1458(tiles, doors);
        Assert.False(devices.Hit(20, 22, WorldWireChannel.Red, (_, _) => { }));
        Assert.Equal((ushort)388, tiles.Get(20, 20).Type);
        occupancy.Free = true;
        Assert.True(devices.Hit(20, 22, WorldWireChannel.Red, (_, _) => { }));
        for (int row = 0; row < 5; row++)
        {
            Assert.Equal((ushort)389, tiles.Get(20, 20 + row).Type);
            Assert.Equal(frameY[row], tiles.Get(20, 20 + row).FrameY);
        }
        Assert.True(devices.Hit(20, 22, WorldWireChannel.Red, (_, _) => { }));
        for (int row = 0; row < 5; row++) Assert.Equal((ushort)388, tiles.Get(20, 20 + row).Type);
    }

    [Fact]
    public void Invalid_coordinates_channel_and_destroyed_light_footprint_are_side_effect_free()
    {
        var tiles = Tiles();
        var cell = Cell(34, 0, 0);
        tiles.Set(20, 20, in cell);
        tiles.Set(21, 20, Cell(1, 0, 0));
        tiles.DirtySections.Clear();
        tiles.PersistenceDirtySections.Clear();
        int published = 0, skipped = 0;
        var devices = new WorldWiringDeviceExecutor1458(tiles,
            publishTileSquare: (_, _, _, _) => published++);
        Assert.False(devices.Hit(20, 20, WorldWireChannel.Red, (_, _) => skipped++));
        Assert.False(devices.Hit(-1, 20, WorldWireChannel.Red, (_, _) => skipped++));
        Assert.False(devices.Hit(20, int.MaxValue, WorldWireChannel.Red, (_, _) => skipped++));
        Assert.False(devices.Hit(20, 20, (WorldWireChannel)99, (_, _) => skipped++));
        Assert.Equal(cell, tiles.Get(20, 20));
        Assert.Equal((ushort)1, tiles.Get(21, 20).Type);
        Assert.Equal(0, published);
        Assert.Equal(0, skipped);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    private static WorldTileStore Tiles() => new(new WorldDimensions(60, 100));
    private static void PutWireSource(WorldTileStore tiles)
    {
        tiles.Set(18, 20, RedCell(136, 0, 0));
        tiles.Set(19, 20, new WorldTile { Flags = WorldTileFlags.WireRed });
    }
    private static WorldTile RedCell(ushort type, short frameX, short frameY)
    {
        WorldTile cell = Cell(type, frameX, frameY);
        cell.Flags &= ~WorldTileFlags.WireYellow;
        return cell;
    }
    private static WorldTile Cell(ushort type, short frameX, short frameY) => new()
    {
        Type = type, FrameX = frameX, FrameY = frameY,
        Flags = WorldTileFlags.Active | WorldTileFlags.WireRed | WorldTileFlags.WireYellow
    };
    private sealed class DoorRandom : IVanillaDoorCloseRandom1458
    {
        public int Calls { get; private set; }
        public int NextClosedDoorFrameColumn() { Calls++; return 1; }
    }
    private sealed class Occupancy(bool free) : IVanillaTallGateOccupancyProbe
    {
        public bool Free { get; set; } = free;
        public int Calls { get; set; }
        public bool IsActorFree(int tileX, int tileY) { Calls++; return Free; }
    }
}
