using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldWiringCircuit1458Tests
{
    [Fact]
    public void Branch_publication_follows_source_fifo_down_up_right_left_and_each_pulse_starts_fresh()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 10, 10, 136, WorldTileFlags.WireRed);
        (int X, int Y)[] expected = [(10, 11), (10, 9), (11, 10), (9, 10)];
        foreach ((int x, int y) in expected) Put(tiles, x, y, 4, WorldTileFlags.WireRed);
        var trace = new List<(int, int)>();
        var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (x, y, _, _) => trace.Add((x, y)));
        var circuit = new WorldWiringCircuit1458(tiles, devices);

        Assert.True(circuit.HitSwitch(10, 10));
        Assert.Equal(expected, trace.ToArray());
        foreach ((int x, int y) in expected) Assert.Equal((short)66, tiles.Get(x, y).FrameX);
        trace.Clear();
        Assert.True(circuit.HitSwitch(10, 10));
        Assert.Equal(expected, trace.ToArray());
        foreach ((int x, int y) in expected) Assert.Equal((short)0, tiles.Get(x, y).FrameX);
    }

    [Fact]
    public void Source_color_order_and_independent_crossings_reach_only_matching_wires()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 10, 10, 136, WorldTileFlagMasks.Wires);
        Put(tiles, 10, 11, 4, WorldTileFlags.WireYellow);
        Put(tiles, 10, 9, 4, WorldTileFlags.WireGreen);
        Put(tiles, 11, 10, 4, WorldTileFlags.WireBlue);
        Put(tiles, 9, 10, 4, WorldTileFlags.WireRed);
        var trace = new List<(int, int)>();
        var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (x, y, _, _) => trace.Add((x, y)));
        var circuit = new WorldWiringCircuit1458(tiles, devices);
        circuit.HitSwitch(10, 10);
        Assert.Equal(new[] { (9, 10), (11, 10), (10, 9), (10, 11) }, trace.ToArray());
        Assert.Equal((short)18, tiles.Get(10, 10).FrameY);
    }

    [Fact]
    public void Ordinary_closed_loop_reaches_a_device_once_without_truncating_the_network()
    {
        var tiles = new WorldTileStore(new WorldDimensions(420, 32));
        Put(tiles, 5, 10, 136, WorldTileFlags.WireRed);
        for (int x = 6; x <= 410; x++)
        {
            Put(tiles, x, 10, 0, WorldTileFlags.WireRed, active: false);
            Put(tiles, x, 12, 0, WorldTileFlags.WireRed, active: false);
        }
        Put(tiles, 6, 11, 0, WorldTileFlags.WireRed, active: false);
        Put(tiles, 410, 11, 4, WorldTileFlags.WireRed);
        int publications = 0;
        var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (_, _, _, _) => publications++);
        var circuit = new WorldWiringCircuit1458(tiles, devices);
        Assert.True(circuit.HitSwitch(5, 10));
        Assert.Equal((short)66, tiles.Get(410, 11).FrameX);
        Assert.Equal(1, publications);
    }

    [Theory]
    [InlineData(0, 10, 8)]
    [InlineData(1, 8, 6)]
    [InlineData(2, 8, 10)]
    public void Junction_frames_route_the_incoming_direction_without_connecting_other_branches(int frame, int targetX, int targetY)
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 5, 8, 136, WorldTileFlags.WireRed);
        Put(tiles, 6, 8, 0, WorldTileFlags.WireRed, active: false);
        Put(tiles, 7, 8, 0, WorldTileFlags.WireRed, active: false);
        Put(tiles, 8, 8, 424, WorldTileFlags.WireRed, frameX: (short)(frame * 18));
        Put(tiles, 9, 8, 0, WorldTileFlags.WireRed, active: false);
        Put(tiles, 8, 7, 0, WorldTileFlags.WireRed, active: false);
        Put(tiles, 8, 9, 0, WorldTileFlags.WireRed, active: false);
        (int X, int Y)[] endpoints = [(10, 8), (8, 6), (8, 10)];
        foreach ((int x, int y) in endpoints) Put(tiles, x, y, 4, WorldTileFlags.WireRed);
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        circuit.HitSwitch(5, 8);
        foreach ((int x, int y) in endpoints)
            Assert.Equal((short)(x == targetX && y == targetY ? 66 : 0), tiles.Get(x, y).FrameX);
    }

    [Theory]
    [InlineData(-18)]
    [InlineData(17)]
    [InlineData(54)]
    public void Malformed_junction_frames_fail_closed_instead_of_fanning_out(int frame)
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 5, 8, 136, WorldTileFlags.WireRed);
        Put(tiles, 6, 8, 424, WorldTileFlags.WireRed, frameX: (short)frame);
        Put(tiles, 7, 8, 4, WorldTileFlags.WireRed);
        Put(tiles, 6, 7, 4, WorldTileFlags.WireRed);
        Put(tiles, 6, 9, 4, WorldTileFlags.WireRed);
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        Assert.True(circuit.HitSwitch(5, 8));
        Assert.Equal((short)0, tiles.Get(7, 8).FrameX);
        Assert.Equal((short)0, tiles.Get(6, 7).FrameX);
        Assert.Equal((short)0, tiles.Get(6, 9).FrameX);
    }

    [Fact]
    public void Pixel_box_collects_both_axes_across_colors_then_toggles_once_after_the_pulse()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 5, 5, 136, WorldTileFlags.WireRed | WorldTileFlags.WireBlue);
        for (int x = 6; x <= 8; x++) Put(tiles, x, 5, 0, WorldTileFlags.WireRed, active: false);
        for (int y = 6; y <= 7; y++) Put(tiles, 8, y, 0, WorldTileFlags.WireRed, active: false);
        for (int y = 6; y <= 8; y++) Put(tiles, 5, y, 0, WorldTileFlags.WireBlue, active: false);
        for (int x = 6; x <= 7; x++) Put(tiles, x, 8, 0, WorldTileFlags.WireBlue, active: false);
        Put(tiles, 8, 8, 445, WorldTileFlags.WireRed | WorldTileFlags.WireBlue);
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        circuit.HitSwitch(5, 5);
        Assert.Equal((short)18, tiles.Get(8, 8).FrameX);
        circuit.HitSwitch(5, 5);
        Assert.Equal((short)0, tiles.Get(8, 8).FrameX);
    }

    [Fact]
    public void Timer_wire_activation_has_no_immediate_output_and_off_on_retains_source_coordinate_clock()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 5, 5, 136, WorldTileFlags.WireRed);
        Put(tiles, 6, 5, 144, WorldTileFlags.WireRed);
        Put(tiles, 6, 6, 4, WorldTileFlags.WireBlue);
        var timer = tiles.Get(6, 5);
        timer.Flags |= WorldTileFlags.WireBlue;
        tiles.Set(6, 5, in timer);
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        circuit.HitSwitch(5, 5);
        Assert.Equal((short)18, tiles.Get(6, 5).FrameY);
        Assert.Equal((short)0, tiles.Get(6, 6).FrameX);
        for (int tick = 1; tick <= 17; tick++) circuit.Tick();
        circuit.HitSwitch(6, 5);
        circuit.HitSwitch(6, 5);
        for (int tick = 18; tick <= 59; tick++) circuit.Tick();
        Assert.Equal((short)0, tiles.Get(6, 6).FrameX);
        circuit.Tick();
        Assert.Equal((short)66, tiles.Get(6, 6).FrameX);
        circuit.Clear();
        Assert.Equal(0, circuit.RegisteredMechanismCount);
        for (int tick = 0; tick < 120; tick++) circuit.Tick();
        Assert.Equal((short)66, tiles.Get(6, 6).FrameX);
    }

    [Fact]
    public void Reentrant_pulse_and_invalid_rectangles_reject_without_damaging_the_active_or_next_pulse()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 5, 5, 136, WorldTileFlags.WireRed);
        Put(tiles, 6, 5, 4, WorldTileFlags.WireRed);
        WorldWiringCircuit1458? circuit = null;
        var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (_, _, _, _) =>
        {
            Assert.False(circuit!.TripWire(5, 5, 1, 1));
            Assert.False(circuit.HitSwitch(5, 5));
        });
        circuit = new WorldWiringCircuit1458(tiles, devices);
        Assert.False(circuit.TripWire(-1, 5, 1, 1));
        Assert.False(circuit.TripWire(1, 1, int.MaxValue, 1));
        Assert.True(circuit.HitSwitch(5, 5));
        Assert.Equal((short)66, tiles.Get(6, 5).FrameX);
        Assert.True(circuit.HitSwitch(5, 5));
        Assert.Equal((short)0, tiles.Get(6, 5).FrameX);
    }

    [Fact]
    public void Damaged_lever_footprint_rejects_before_changing_any_cell_or_starting_a_pulse()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 5, 5, 132, WorldTileFlags.WireRed);
        Put(tiles, 6, 5, 132, WorldTileFlags.WireRed, frameX: 18);
        Put(tiles, 5, 6, 132, WorldTileFlags.WireRed);
        Put(tiles, 6, 6, 4, WorldTileFlags.WireRed);
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        Assert.False(circuit.HitSwitch(5, 5));
        Assert.Equal((short)0, tiles.Get(5, 5).FrameX);
        Assert.Equal((short)18, tiles.Get(6, 5).FrameX);
        Assert.Equal((short)0, tiles.Get(6, 6).FrameX);
    }

    [Fact]
    public void Destroyed_and_reinstalled_timer_retains_the_official_coordinate_countdown_until_expiry()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 5, 5, 144, WorldTileFlags.WireRed);
        Put(tiles, 6, 5, 4, WorldTileFlags.WireRed);
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        circuit.HitSwitch(5, 5);
        for (int tick = 1; tick <= 17; tick++) circuit.Tick();
        Put(tiles, 5, 5, 0, WorldTileFlags.WireRed, active: false);
        for (int tick = 18; tick <= 30; tick++) circuit.Tick();
        Assert.Equal(1, circuit.RegisteredMechanismCount);
        Put(tiles, 5, 5, 144, WorldTileFlags.WireRed);
        circuit.HitSwitch(5, 5);
        for (int tick = 31; tick <= 59; tick++) circuit.Tick();
        Assert.Equal((short)0, tiles.Get(6, 5).FrameX);
        circuit.Tick();
        Assert.Equal((short)66, tiles.Get(6, 5).FrameX);
        Put(tiles, 5, 5, 0, WorldTileFlags.WireRed, active: false);
        for (int tick = 0; tick < 18000; tick++) circuit.Tick();
        Assert.Equal(0, circuit.RegisteredMechanismCount);
        Assert.Equal((short)66, tiles.Get(6, 5).FrameX);
    }

    [Fact]
    public void Mechanism_registration_has_the_source_999_slot_ceiling_and_reuses_retired_slots()
    {
        var tiles = new WorldTileStore(new WorldDimensions(60, 60));
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        for (int i = 0; i < 1000; i++)
        {
            int x = i / 50 + 2;
            int y = i % 50 + 2;
            Put(tiles, x, y, 144, 0);
            Assert.True(circuit.HitSwitch(x, y));
        }
        Assert.Equal(999, circuit.RegisteredMechanismCount);
        Assert.Equal((short)18, tiles.Get(21, 51).FrameY);
        circuit.HitSwitch(2, 2);
        circuit.Tick();
        Assert.Equal(998, circuit.RegisteredMechanismCount);
        circuit.HitSwitch(21, 51);
        circuit.HitSwitch(21, 51);
        Assert.Equal(999, circuit.RegisteredMechanismCount);
    }

    [Fact]
    public void Simultaneous_timer_pulses_publish_in_reverse_registration_order()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        Put(tiles, 5, 5, 144, WorldTileFlags.WireRed);
        Put(tiles, 6, 5, 4, WorldTileFlags.WireRed);
        Put(tiles, 15, 15, 144, WorldTileFlags.WireRed);
        Put(tiles, 16, 15, 4, WorldTileFlags.WireRed);
        var trace = new List<(int, int)>();
        var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (x, y, _, _) => trace.Add((x, y)));
        var circuit = new WorldWiringCircuit1458(tiles, devices);
        circuit.HitSwitch(5, 5);
        circuit.HitSwitch(15, 15);
        for (int tick = 0; tick < 60; tick++) circuit.Tick();
        Assert.Equal(new[] { (16, 15), (6, 5) }, trace.ToArray());
    }

    [Fact]
    public void Edge_timer_registrations_use_source_one_tile_inset_and_drop_the_last_registered_entry()
    {
        (int X, int Y)[] edges = [(0, 5), (23, 5), (5, 0), (5, 23)];
        foreach ((int x, int y) in edges)
        {
            var tiles = new WorldTileStore(new WorldDimensions(24, 24));
            Put(tiles, x, y, 144, WorldTileFlags.WireRed);
            var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
            Assert.True(circuit.HitSwitch(x, y));
            Assert.Equal(1, circuit.RegisteredMechanismCount);
            circuit.Tick();
            Assert.Equal(0, circuit.RegisteredMechanismCount);
            // The source invalid-coordinate branch drops bookkeeping without turning off the frame.
            Assert.Equal((short)18, tiles.Get(x, y).FrameY);
        }

        var mixed = new WorldTileStore(new WorldDimensions(24, 24));
        Put(mixed, 0, 5, 144, WorldTileFlags.WireRed);
        Put(mixed, 10, 10, 144, WorldTileFlags.WireRed);
        Put(mixed, 11, 10, 4, WorldTileFlags.WireRed);
        var registrations = new WorldWiringCircuit1458(mixed, new WorldWiringDeviceExecutor1458(mixed));
        registrations.HitSwitch(0, 5);
        registrations.HitSwitch(10, 10);
        registrations.Tick();
        Assert.Equal(1, registrations.RegisteredMechanismCount);
        // Source table truncation drops the newer interior registration. The retained edge entry
        // is dropped on the next tick, so the interior timer cannot emit a pulse at its old deadline.
        registrations.Tick();
        Assert.Equal(0, registrations.RegisteredMechanismCount);
        for (int tick = 2; tick < 60; tick++) registrations.Tick();
        Assert.Equal((short)0, mixed.Get(11, 10).FrameX);
        Assert.Equal((short)18, mixed.Get(10, 10).FrameY);
    }

    [Fact]
    public void Repeated_momentary_switch_press_keeps_the_old_deadline_and_expiry_flips_the_current_frame_without_a_pulse()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            var tile = new WorldTile { Type = 411, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
                FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) };
            tiles.Set(5 + dx, 5 + dy, in tile);
        }
        Put(tiles, 7, 5, 4, WorldTileFlags.WireRed);
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        Assert.True(circuit.HitSwitch(6, 6));
        Assert.Equal((short)36, tiles.Get(5, 5).FrameX);
        Assert.Equal((short)66, tiles.Get(7, 5).FrameX);
        for (int tick = 0; tick < 30; tick++) circuit.Tick();
        Assert.True(circuit.HitSwitch(5, 5));
        Assert.Equal((short)0, tiles.Get(5, 5).FrameX);
        Assert.Equal((short)0, tiles.Get(7, 5).FrameX);
        Assert.Equal(1, circuit.RegisteredMechanismCount);
        for (int tick = 30; tick < 59; tick++) circuit.Tick();
        Assert.Equal((short)0, tiles.Get(5, 5).FrameX);
        circuit.Tick();
        Assert.Equal((short)36, tiles.Get(5, 5).FrameX);
        Assert.Equal((short)0, tiles.Get(7, 5).FrameX);
        Assert.Equal(0, circuit.RegisteredMechanismCount);
    }

    private static void Put(WorldTileStore tiles, int x, int y, ushort type, WorldTileFlags wires,
        bool active = true, short frameX = 0)
    {
        var tile = new WorldTile { Type = type, Flags = wires | (active ? WorldTileFlags.Active : 0), FrameX = frameX };
        tiles.Set(x, y, in tile);
    }
}
