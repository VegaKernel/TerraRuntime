using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldWiringOriginal1458Tests
{
    [Fact]
    public void Actual_official_switch_lever_timer_light_actuator_and_actor_scenes_match_the_existing_tile_owner()
    {
        // Unmodified official Linux 1.4.5.8 binary, executed on Windows CoreCLR; this is not Linux execution.
        // The independent probe invokes Wiring.HitSwitch/UpdateMech and Collision.SwitchTiles by reflection.
        using Stream resource = typeof(WorldWiringOriginal1458Tests).Assembly.GetManifestResourceStream("WiringOriginalLinux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034", document.RootElement.GetProperty("sha256").GetString());
        int count = 0;
        foreach (JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            count++;
            var tiles = new WorldTileStore(new WorldDimensions(80, 100));
            var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
            string kind = row.GetProperty("kind").GetString()!;
            if (kind is "switch" or "switchRepeat" or "lever")
            {
                int colors = row.GetProperty("colors").GetInt32();
                if (kind == "lever")
                {
                    for (int x = 20; x < 22; x++)
                    for (int y = 20; y < 22; y++) Put(tiles, x, y, 132, (x - 20) * 18, (y - 20) * 18, colors);
                    Put(tiles, 22, 20, 4, colors: colors);
                    Assert.True(circuit.HitSwitch(21, 21));
                    int[] coordinatesX = [20, 21, 20, 21], coordinatesY = [20, 20, 21, 21];
                    int i = 0;
                    foreach (JsonElement frame in row.GetProperty("frames").EnumerateArray())
                    { Assert.Equal(frame.GetInt16(), tiles.Get(coordinatesX[i], coordinatesY[i]).FrameX); i++; }
                    Assert.Equal(row.GetProperty("torchFrameX").GetInt16(), tiles.Get(22, 20).FrameX);
                }
                else
                {
                    Put(tiles, 20, 20, 136, colors: colors); Put(tiles, 21, 20, 4, colors: colors);
                    Assert.True(circuit.HitSwitch(20, 20));
                    if (kind == "switchRepeat") Assert.True(circuit.HitSwitch(20, 20));
                    Assert.Equal(row.GetProperty("switchFrameY").GetInt16(), tiles.Get(20, 20).FrameY);
                    Assert.Equal(row.GetProperty("torchFrameX").GetInt16(), tiles.Get(21, 20).FrameX);
                }
            }
            else if (kind is "timer" or "timerOffOnSameTick")
            {
                int style = kind == "timer" ? row.GetProperty("style").GetInt32() : 0;
                Put(tiles, 20, 20, 144, style * 18); Put(tiles, 21, 20, 4);
                Assert.True(circuit.HitSwitch(20, 20));
                var expected = row.GetProperty("changes").EnumerateArray().ToArray();
                int max = expected[^1].GetProperty("tick").GetInt32() + 1, changeIndex = 0;
                short previous = 0;
                for (int tick = 1; tick <= max; tick++)
                {
                    if (kind == "timerOffOnSameTick" && tick == 18)
                    { Assert.True(circuit.HitSwitch(20, 20)); Assert.True(circuit.HitSwitch(20, 20)); }
                    circuit.Tick();
                    short current = tiles.Get(21, 20).FrameX;
                    if (current == previous) continue;
                    Assert.True(changeIndex < expected.Length);
                    Assert.Equal(expected[changeIndex].GetProperty("tick").GetInt32(), tick);
                    Assert.Equal(expected[changeIndex].GetProperty("frameX").GetInt16(), current);
                    previous = current; changeIndex++;
                }
                Assert.Equal(expected.Length, changeIndex);
                Assert.Equal((short)18, tiles.Get(20, 20).FrameY);
            }
            else if (kind == "light")
            {
                int width = row.GetProperty("width").GetInt32(), height = row.GetProperty("height").GetInt32();
                int style = row.GetProperty("style").GetInt32();
                ushort type = row.GetProperty("type").GetUInt16();
                Put(tiles, 20, 20, 136);
                for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++) Put(tiles, 21 + x, 20 + y, type, style + x * 18, y * 18);
                Assert.True(circuit.HitSwitch(20, 20));
                JsonElement[] frames = row.GetProperty("frames").EnumerateArray().ToArray();
                int index = 0;
                for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++) Assert.Equal(frames[index++].GetInt16(), tiles.Get(21 + x, 20 + y).FrameX);
            }
            else if (kind == "actuator")
            {
                bool downed = row.GetProperty("down").GetBoolean();
                circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles,
                    plantBossDowned: () => downed, worldSurface: 10));
                Put(tiles, 20, 20, 136); Put(tiles, 21, 20, row.GetProperty("type").GetUInt16());
                WorldTile tile = tiles.Get(21, 20); tile.Flags |= WorldTileFlags.Actuator; tiles.Set(21, 20, in tile);
                Assert.True(circuit.HitSwitch(20, 20));
                Assert.Equal(row.GetProperty("actuated").GetBoolean(), tiles.Get(21, 20).IsActuated);
            }
            else
            {
                Assert.Equal("plate", kind);
                int style = row.GetProperty("style").GetInt32();
                Put(tiles, 20, 20, 135, 0, style * 18); Put(tiles, 21, 20, 4);
                bool found = WorldPressurePlateContacts1458.TryFindContact(tiles, 320, 301, 16, 32, 320, 300,
                    (WorldPressurePlateActor1458)row.GetProperty("actor").GetByte(), out var contact);
                Assert.Equal(row.GetProperty("triggered").GetBoolean(), found);
                if (found)
                {
                    Assert.Equal(20, contact.X); Assert.Equal(20, contact.Y);
                    Assert.Equal(style == 7, contact.DestroyAfterPulse);
                    Assert.True(circuit.HitSwitch(contact.X, contact.Y));
                }
                // Source style7 kills the plate after pulsing and reframes its adjacent torch. This
                // test admits actor/contact and pulse only; the production kill/drop path stays excluded.
                if (!found || !contact.DestroyAfterPulse)
                    Assert.Equal(row.GetProperty("torchFrameX").GetInt16(), tiles.Get(21, 20).FrameX);
                else
                {
                    Assert.False(row.GetProperty("plateActive").GetBoolean());
                    Assert.Equal((short)162, row.GetProperty("torchFrameX").GetInt16());
                    Assert.Equal((short)66, tiles.Get(21, 20).FrameX); // Pulse only, before unsupported kill/reframe.
                }
            }
        }
        Assert.Equal(98, count);
    }

    [Fact]
    public void Plate_contact_requires_new_overlap_and_excludes_inactive_tiles_and_bottom_rows()
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 100));
        Put(tiles, 20, 20, 135);
        Assert.False(Contact(tiles, 320, 300, 320, 299)); // Bottom equals plate top.
        Assert.True(Contact(tiles, 320, 301, 320, 300));
        Assert.False(Contact(tiles, 320, 301, 320, 301)); // No fresh overlap.
        Assert.False(Contact(tiles, 304, 301, 304, 300)); // Right equals plate left.
        Assert.False(Contact(tiles, 336, 301, 336, 300));
        Assert.True(Contact(tiles, 305, 301, 304, 300));
        Assert.False(Contact(tiles, float.NaN, 301, 320, 300));
        WorldTile inactive = tiles.Get(20, 20); inactive.Flags &= ~WorldTileFlags.Active; tiles.Set(20, 20, in inactive);
        Assert.False(Contact(tiles, 320, 301, 320, 300));
        Put(tiles, 20, 60, 135);
        Assert.False(Contact(tiles, 320, 941, 320, 940));
    }

    [Fact]
    public void Actual_delegated_WorldGen_footprints_toggle_once_per_color_and_preserve_complete_object_images()
    {
        using JsonDocument reference = LoadReference();
        int count = 0;
        foreach (JsonElement row in reference.RootElement.GetProperty("doorScenes").EnumerateArray())
        {
            count++;
            var tiles = new WorldTileStore(new WorldDimensions(80, 100));
            string family = row.GetProperty("family").GetString()!;
            int sourceX = row.GetProperty("sourceX").GetInt32();
            Put(tiles, sourceX, 20, 136);
            for (int y = 21; y < 25; y++) tiles.Set(20, y, new WorldTile { Flags = WorldTileFlags.WireRed });
            if (family == "trapdoor") { Put(tiles, 20, 20, 1); Put(tiles, 23, 20, 1, colors: 0); }
            else { Put(tiles, 21, 19, 1, colors: 0); Put(tiles, 21, family == "door" ? 23 : 25, 1, colors: 0); }
            foreach (JsonElement cell in row.GetProperty("before").EnumerateArray())
            {
                int colors = cell.GetProperty("colors").GetInt32();
                WorldTileFlags flags = cell.GetProperty("active").GetBoolean() ? WorldTileFlags.Active : 0;
                for (int i = 0; i < 4; i++) if ((colors & (1 << i)) != 0) flags |= (WorldTileFlags)(1 << (i + 1));
                tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), new WorldTile
                { Type = cell.GetProperty("type").GetUInt16(), FrameX = cell.GetProperty("frameX").GetInt16(),
                    FrameY = cell.GetProperty("frameY").GetInt16(), Flags = flags });
            }
            var occupancy = new ReferenceOccupancy(!row.GetProperty("blocked").GetBoolean());
            var frameRandom = new ReferenceDoorFrames(row.GetProperty("closeRolls").EnumerateArray().Select(v => v.GetInt32()).ToArray());
            int directionCalls = 0;
            int direction = family == "door" && row.GetProperty("directionRolls")[0].GetInt32() == 0 ? -1 : 1;
            var doors = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupancy, frameRandom);
            var device = new WorldWiringDeviceExecutor1458(tiles, doors, nextDoorDirection: () => { directionCalls++; return direction; });
            var circuit = new WorldWiringCircuit1458(tiles, device);
            Assert.True(circuit.HitSwitch(sourceX, 20));
            AssertImage(tiles, row.GetProperty("first"));
            Assert.Equal(family == "door" ? 1 : 0, directionCalls);
            Assert.Equal(0, frameRandom.Calls);
            occupancy.Free = true; // The blocked gate's second pulse follows actor departure.
            Assert.True(circuit.HitSwitch(sourceX, 20));
            AssertImage(tiles, row.GetProperty("second"));
            Assert.Equal(family == "door" ? 1 : 0, directionCalls);
            Assert.Equal(family == "door" ? 3 : 0, frameRandom.Calls);
        }
        Assert.Equal(8, count);
    }

    [Fact]
    public void Actual_edge_timer_registration_traces_preserve_source_table_truncation_order()
    {
        using JsonDocument reference = LoadReference();
        int count = 0;
        foreach (JsonElement row in reference.RootElement.GetProperty("timerEdges").EnumerateArray())
        {
            count++;
            var tiles = new WorldTileStore(new WorldDimensions(80, 100));
            var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
            Put(tiles, 0, 20, 144); Put(tiles, 20, 20, 144); Put(tiles, 21, 20, 4);
            string registration = row.GetProperty("registration").GetString()!;
            if (registration == "interiorThenEdge") Assert.True(circuit.HitSwitch(20, 20));
            Assert.True(circuit.HitSwitch(0, 20));
            if (registration == "edgeThenInterior") Assert.True(circuit.HitSwitch(20, 20));
            int tick = 0;
            foreach (JsonElement image in row.GetProperty("trace").EnumerateArray())
            {
                int target = image.GetProperty("tick").GetInt32();
                while (tick < target) { circuit.Tick(); tick++; }
                Assert.Equal(image.GetProperty("count").GetInt32(), circuit.RegisteredMechanismCount);
                Assert.Equal(image.GetProperty("edgeFrameY").GetInt16(), tiles.Get(0, 20).FrameY);
                Assert.Equal(image.GetProperty("interiorFrameY").GetInt16(), tiles.Get(20, 20).FrameY);
                Assert.Equal(image.GetProperty("torchFrameX").GetInt16(), tiles.Get(21, 20).FrameX);
            }
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void Actual_momentary_repeated_click_uses_the_original_expiration_and_current_frame_direction()
    {
        using JsonDocument reference = LoadReference();
        JsonElement expected = reference.RootElement.GetProperty("momentary");
        var tiles = new WorldTileStore(new WorldDimensions(80, 100));
        var circuit = new WorldWiringCircuit1458(tiles, new WorldWiringDeviceExecutor1458(tiles));
        for (int x = 20; x < 22; x++)
        for (int y = 20; y < 22; y++) Put(tiles, x, y, 411, (x - 20) * 18, (y - 20) * 18);
        Assert.True(circuit.HitSwitch(20, 20));
        for (int tick = 0; tick < 30; tick++) circuit.Tick();
        Assert.True(circuit.HitSwitch(20, 20));
        Assert.Equal(expected.GetProperty("at30").GetInt16(), tiles.Get(20, 20).FrameX);
        for (int tick = 30; tick < 60; tick++) circuit.Tick();
        Assert.Equal(expected.GetProperty("at60").GetInt16(), tiles.Get(20, 20).FrameX);
        Assert.Equal(expected.GetProperty("count").GetInt32(), circuit.RegisteredMechanismCount);
    }

    private static JsonDocument LoadReference()
    {
        using Stream resource = typeof(WorldWiringOriginal1458Tests).Assembly.GetManifestResourceStream("WiringOriginalLinux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void AssertImage(WorldTileStore tiles, JsonElement image)
    {
        foreach (JsonElement cell in image.EnumerateArray())
        {
            WorldTile tile = tiles.Get(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32());
            Assert.Equal(cell.GetProperty("active").GetBoolean(), tile.IsActive);
            Assert.Equal(cell.GetProperty("type").GetUInt16(), tile.Type);
            Assert.Equal(cell.GetProperty("frameX").GetInt16(), tile.FrameX);
            Assert.Equal(cell.GetProperty("frameY").GetInt16(), tile.FrameY);
            int colors = 0;
            for (int i = 0; i < 4; i++) if ((tile.Flags & (WorldTileFlags)(1 << (i + 1))) != 0) colors |= 1 << i;
            Assert.Equal(cell.GetProperty("colors").GetInt32(), colors);
        }
    }

    private sealed class ReferenceOccupancy(bool free) : IVanillaTallGateOccupancyProbe
    { public bool Free { get; set; } = free; public bool IsActorFree(int x, int y) => Free; }

    // The source capture observes the three offered closing columns before CloseDoor runs.
    // Replaying those offers checks transform/draw count and order, not whole-world framing RNG parity.
    private sealed class ReferenceDoorFrames(int[] columns) : IVanillaDoorCloseRandom1458
    {
        public int Calls { get; private set; }
        public int NextClosedDoorFrameColumn() { int call = Calls++; return columns.Length == 0 ? 0 : columns[call % columns.Length]; }
    }

    private static bool Contact(WorldTileStore tiles, float x, float y, float oldX, float oldY) =>
        WorldPressurePlateContacts1458.TryFindContact(tiles, x, y, 16, 32, oldX, oldY, WorldPressurePlateActor1458.Player, out _);

    private static void Put(WorldTileStore tiles, int x, int y, ushort type, int frameX = 0, int frameY = 0, int colors = 1)
    {
        WorldTileFlags flags = WorldTileFlags.Active;
        for (int i = 0; i < 4; i++) if ((colors & (1 << i)) != 0) flags |= (WorldTileFlags)(1 << (i + 1));
        tiles.Set(x, y, new WorldTile { Type = type, FrameX = (short)frameX, FrameY = (short)frameY, Flags = flags });
    }
}
