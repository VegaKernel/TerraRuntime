using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringAnimatedDevice1458Tests
{
    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic;
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(WiringAnimatedDevice1458Tests).Assembly.GetManifestResourceStream("WiringAnimatedDevice1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            foreach (bool composed in new[] { false, true }) yield return [row.GetRawText(), composed];
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Conversion_toggles_animation_order_and_timed_logic_match_unchanged_original(string observation, bool composed)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100));
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(8192, 4194304, 8192));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(145877), new(new PlayerSlotId(1), new(1)));
        Assert.True(replication.TryRegister(connection.Source, queue));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, in spawn);
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random));
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Access)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Access)!.GetValue(graph.WorldTileAuthority)!;
        var frames = new List<string>();
        if (!composed)
        {
            void Square(int x, int y, byte width, byte height)
            {
                Assert.True(TerrariaTileSquareCodec.TryEncode(tiles, x, y, width, height, VanillaTileChangeType1458.None, out var frame));
                frames.Add(Convert.ToHexString(frame));
            }
            var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: Square,
                publishTemporaryAnimation: (animation, tile, x, y) =>
                {
                    Assert.True(TerrariaTemporaryAnimationCodec1458.TryEncode(animation, tile, x, y, out var frame));
                    frames.Add(Convert.ToHexString(frame));
                });
            circuit = new(tiles, devices, Square, random.Next, (x, y) =>
            {
                VanillaPoofOfSmoke1458.ConsumeDedicatedSourceOffers(new SystemVanillaNpcRandom(random));
                Assert.True(TerrariaPoofOfSmokeCodec1458.TryEncode(x, y, out var frame));
                frames.Add(Convert.ToHexString(frame));
            });
        }
        foreach (var cell in row.GetProperty("before").GetProperty("cells").EnumerateArray())
        {
            var tile = DecodeCell(cell.GetProperty("tile"));
            tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), in tile);
        }
        Drain(queue); int tick = 0;
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            frames.Clear(); Drain(queue);
            while (tick < point.GetProperty("tick").GetInt32()) { circuit.Tick(); tick++; }
            if (point.GetProperty("trigger").GetBoolean())
                Assert.True(circuit.HitSwitch(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32()));
            Assert.False(circuit.LastLogicBudgetExceeded);
            foreach (var cell in point.GetProperty("cells").EnumerateArray())
            {
                int x = cell.GetProperty("x").GetInt32(), y = cell.GetProperty("y").GetInt32();
                var expected = DecodeCell(cell.GetProperty("tile")); var actual = tiles.Get(x, y);
                Assert.True(expected.Equals(actual), $"({x},{y}) expected type/frame/flags/shape={expected.Type}/{expected.FrameX},{expected.FrameY}/{expected.Flags}/{expected.Shape}; actual={actual.Type}/{actual.FrameX},{actual.FrameY}/{actual.Flags}/{actual.Shape}");
            }
            int count = 0;
            for (int x = 0; x < 80; x++) for (int y = 0; y < 100; y++)
            {
                var tile = tiles.Get(x, y);
                if (tile.IsActive || (tile.Flags & WorldTileFlagMasks.Wires) != 0) count++;
            }
            Assert.Equal(point.GetProperty("cells").GetArrayLength(), count);
            Assert.Equal(point.GetProperty("mechs").GetInt32(), circuit.RegisteredMechanismCount);
            var rng = point.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Access)!.GetValue(random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Access)!.GetValue(random)!);
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!), composed ? Drain(queue) : frames);
        }
    }

    private static WorldTile DecodeCell(JsonElement image)
    {
        var tile = WiringLogicGate1458Tests.Decode(image);
        tile.Wall = image.GetProperty("wall").GetUInt16();
        tile.TileColor = image.GetProperty("tileColor").GetByte();
        tile.WallColor = image.GetProperty("wallColor").GetByte();
        Assert.True(tile.TrySetSourceShape(image.GetProperty("halfBrick").GetBoolean(), image.GetProperty("slope").GetByte()));
        if (image.GetProperty("invisibleBlock").GetBoolean()) tile.Flags |= WorldTileFlags.InvisibleBlock;
        if (image.GetProperty("invisibleWall").GetBoolean()) tile.Flags |= WorldTileFlags.InvisibleWall;
        if (image.GetProperty("fullbrightBlock").GetBoolean()) tile.Flags |= WorldTileFlags.FullbrightBlock;
        if (image.GetProperty("fullbrightWall").GetBoolean()) tile.Flags |= WorldTileFlags.FullbrightWall;
        return tile;
    }

    private static string[] Drain(TerrariaConnectionOutboundQueue queue) =>
        GemBunnyWire1458Tests.Drain(queue).Select(Convert.ToHexString).ToArray();
}
