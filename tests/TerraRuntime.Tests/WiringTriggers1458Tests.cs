using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringTriggers1458Tests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IEnumerable<object[]> WeightedRows() => Rows("weights");
    public static IEnumerable<object[]> SensorRows() => Rows("sensors");

    private static IEnumerable<object[]> Rows(string collection)
    {
        using var resource = typeof(WiringTriggers1458Tests).Assembly.GetManifestResourceStream("WiringTriggersLinux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty(collection).EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(WeightedRows))]
    public void Weighted_occupancy_releases_frames_packets_and_rng_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = CreateTiles(row.GetProperty("colors").GetInt32(), 428);
        bool loaded = row.GetProperty("loaded").GetBoolean();
        Assert.True(tiles.TryAttachPressurePlates(loaded ? [new(30, 40)] : [], 16, loaded));
        var pressure = tiles.RuntimePressurePlates!;
        var frames = new List<string>(); var random = new VanillaUnifiedRandom1458(1458);
        var circuit = CreateCircuit(tiles, frames, random);
        void Pulse(int x, int y)
        {
            Assert.True(circuit.HitSwitchWithoutPlayerTeleportation(x, y));
            frames.Add(Convert.ToHexString(TerrariaHitSwitchCodec1458.Encode(new((short)x, (short)y))));
        }
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            frames.Clear();
            var player = new PlayerHandle(new PlayerSlotId(point.GetProperty("slot").GetByte()), new(1));
            switch (point.GetProperty("op").GetString())
            {
                case "move": Assert.True(pressure.Update(player, point.GetProperty("x").GetSingle(), point.GetProperty("y").GetSingle(), 20, 42, Pulse)); break;
                case "reset": pressure.ResetPlayer(player, Pulse); break;
                case "update": pressure.FirstUpdate(Pulse); break;
                case "destroy": pressure.Destroy(30, 40, Pulse); break;
                default: Assert.Fail("Unknown captured operation."); break;
            }
            var keys = point.GetProperty("pressed").EnumerateArray().ToArray();
            Assert.Equal(keys.Select(p => new WorldPressurePlate(p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32())), pressure.CaptureSnapshot());
            var occupancy = (Dictionary<(int X, int Y), HashSet<PlayerHandle>?>)typeof(WorldWeightedPressurePlates1458).GetField("pressed", Hidden)!.GetValue(pressure)!;
            foreach (var key in keys)
            {
                var holders = occupancy[(key.GetProperty("x").GetInt32(), key.GetProperty("y").GetInt32())];
                Assert.Equal(key.GetProperty("slots").EnumerateArray().Select(x => x.GetByte()), holders is null ? Array.Empty<byte>() : holders.Select(p => p.Slot.Value).Order());
            }
            Assert.Equal(point.GetProperty("frame").GetInt16(), tiles.Get(33, 40).FrameX);
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(x => x.GetString()), frames);
            AssertRandom(point.GetProperty("random"), random);
        }
    }

    [Theory, MemberData(nameof(SensorRows))]
    public void Sensor_hysteresis_edges_frames_packets_and_rng_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        byte check = row.GetProperty("check").GetByte(); bool on = row.GetProperty("initial").GetBoolean(); int counted = 0;
        var tiles = CreateTiles(row.GetProperty("colors").GetInt32(), 423);
        var tile = tiles.Get(30, 40); tile.FrameX = on ? (short)18 : (short)0; tile.FrameY = (short)((check - 1) * 18); tiles.Set(30, 40, in tile);
        Assert.True(tiles.TryAttachTileEntities([new(0, 30, 40, WorldTileEntityKind.LogicSensor, new WorldLogicSensorPayload(check, on))], 16));
        var frames = new List<string>(); var random = new VanillaUnifiedRandom1458(1458);
        var circuit = CreateCircuit(tiles, frames, random);
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            frames.Clear(); tile = tiles.Get(30, 40);
            tile.LiquidAmount = point.GetProperty("amount").GetByte(); tile.LiquidKind = (WorldLiquidKind)point.GetProperty("kind").GetByte(); tiles.Set(30, 40, in tile);
            bool above = point.GetProperty("active").GetBoolean() && !point.GetProperty("dead").GetBoolean() && !point.GetProperty("ghost").GetBoolean() &&
                VanillaLogicSensor1458.PlayerAbove(30, 40, point.GetProperty("px").GetSingle(), point.GetProperty("py").GetSingle(), 20, 42);
            Assert.True(VanillaLogicSensor1458.TryStep(check, on, counted, point.GetProperty("day").GetBoolean(), above,
                tile.LiquidAmount, tile.LiquidKind == WorldLiquidKind.Lava, tile.LiquidKind == WorldLiquidKind.Honey, tile.LiquidKind == WorldLiquidKind.Shimmer, out var step));
            if (step.Changed)
            {
                var metadata = tiles.RuntimeTileEntities!;
                var sensor = Assert.Single(metadata.CaptureSnapshot());
                Assert.True(metadata.TrySetLogicSensorState(sensor, in tile, step.IsOn));
                Publish(tiles, frames, 30, 40, 1, 1);
            }
            if (step.TripWire)
            {
                Assert.True(step.BlockPlayers ? circuit.HitSwitchWithoutPlayerTeleportation(30, 40) : circuit.HitSwitch(30, 40));
                frames.Add(Convert.ToHexString(TerrariaHitSwitchCodec1458.Encode(new(30, 40))));
            }
            on = step.IsOn; counted = step.CountedData;
            Assert.Equal(point.GetProperty("on").GetBoolean(), on);
            Assert.Equal(point.GetProperty("counted").GetInt32(), counted);
            Assert.Equal(point.GetProperty("frame").GetInt16(), tiles.Get(30, 40).FrameX);
            Assert.Equal(point.GetProperty("torch").GetInt16(), tiles.Get(33, 40).FrameX);
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(x => x.GetString()), frames);
            AssertRandom(point.GetProperty("random"), random);
        }
    }

    private static WorldTileStore CreateTiles(int colors, ushort type)
    {
        var tiles = new WorldTileStore(new(80, 100)); WorldTileFlags flags = default;
        if ((colors & 1) != 0) flags |= WorldTileFlags.WireRed;
        if ((colors & 2) != 0) flags |= WorldTileFlags.WireBlue;
        if ((colors & 4) != 0) flags |= WorldTileFlags.WireGreen;
        if ((colors & 8) != 0) flags |= WorldTileFlags.WireYellow;
        for (int x = 30; x <= 33; x++) tiles.Set(x, 40, new() { Flags = flags });
        tiles.Set(30, 40, new() { Type = type, Flags = flags | WorldTileFlags.Active });
        tiles.Set(33, 40, new() { Type = 4, Flags = flags | WorldTileFlags.Active });
        return tiles;
    }

    private static WorldWiringCircuit1458 CreateCircuit(WorldTileStore tiles, List<string> frames, VanillaUnifiedRandom1458 random)
    {
        void Changed(int x, int y, byte width, byte height) => Publish(tiles, frames, x, y, width, height);
        return new(tiles, new(tiles, publishTileSquare: Changed), Changed, random.Next);
    }

    private static void Publish(WorldTileStore tiles, List<string> frames, int x, int y, byte width, byte height)
    {
        Assert.True(TerrariaTileSquareCodec.TryEncode(tiles, x, y, width, height, VanillaTileChangeType1458.None, out var frame));
        frames.Add(Convert.ToHexString(frame));
    }

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Hidden)!.GetValue(random)));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Hidden)!.GetValue(random)!);
    }
}
