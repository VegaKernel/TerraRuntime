using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringPump1458Tests
{
    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic;
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(WiringPump1458Tests).Assembly.GetManifestResourceStream("WiringPump1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Pump_material_order_caps_queues_rng_and_legacy_projection_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100)); var random = new VanillaUnifiedRandom1458(1458);
        var registry = new RuntimeTileManipulationReplicationRegistry();
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(8192, 4194304, 8192));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1458142), new(new PlayerSlotId(1), new(1)));
        Assert.True(registry.TryRegister(connection.Source, queue));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0); registry.PlayerSpawned(connection, in spawn);
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: registry,
            naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Access)!.GetValue(state)!;
        var owner = graph.WorldTileAuthority;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Access)!.GetValue(owner)!;
        var simulator = (VanillaWorldLiquidSimulator1458)typeof(WorldTileAuthority).GetField("liquidSimulator", Access)!.GetValue(owner)!;
        typeof(VanillaWorldLiquidSimulator1458).GetProperty("CurrentMaximumLiquid1458", Access | BindingFlags.Public)!.SetValue(simulator, row.GetProperty("cap").GetInt32());
        var before = row.GetProperty("before");
        foreach (var c in before.GetProperty("cells").EnumerateArray())
        {
            var tile = WiringLogicGate1458Tests.Decode(c.GetProperty("tile"));
            tile.LiquidAmount = c.GetProperty("amount").GetByte(); tile.LiquidKind = (WorldLiquidKind)c.GetProperty("kind").GetByte();
            tiles.Set(c.GetProperty("x").GetInt32(), c.GetProperty("y").GetInt32(), in tile);
        }
        var active = Coordinates(before.GetProperty("active")).Select(c => new WorldLiquidUpdateEntry(c.X * 100 + c.Y, 0, 0)).ToArray();
        var buffered = Coordinates(before.GetProperty("buffered")).Select(c => c.X * 100 + c.Y).ToArray();
        Assert.True(tiles.LiquidUpdates.TryRestoreSnapshot(active, buffered));
        foreach (var c in Coordinates(before.GetProperty("network")))
        {
            var tile = tiles.Get(c.X, c.Y); var pending = new TerrariaLiquidState((short)c.X, (short)c.Y, tile.LiquidAmount, (byte)tile.LiquidKind);
            Assert.True(registry.RetainSourceLiquid(in pending));
        }
        Drain(queue);
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            Assert.True(circuit.HitSwitch(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32()));
            var expected = point.GetProperty("state");
            foreach (var c in expected.GetProperty("cells").EnumerateArray())
            {
                int x = c.GetProperty("x").GetInt32(), y = c.GetProperty("y").GetInt32(); var actual = tiles.Get(x, y);
                Assert.Equal(c.GetProperty("amount").GetByte(), actual.LiquidAmount); Assert.Equal(c.GetProperty("kind").GetByte(), (byte)actual.LiquidKind);
                Assert.Equal(c.GetProperty("active").GetBoolean(), actual.IsActive); Assert.Equal(c.GetProperty("type").GetUInt16(), actual.Type);
                if (actual.Type is 142 or 143 or 136)
                { Assert.Equal(c.GetProperty("fx").GetInt16(), actual.FrameX); Assert.Equal(c.GetProperty("fy").GetInt16(), actual.FrameY); }
                Assert.Equal(c.GetProperty("checking").GetBoolean(), tiles.LiquidUpdates.IsCheckingLiquid(x, y));
                Assert.Equal(c.GetProperty("skip").GetBoolean(), tiles.LiquidUpdates.IsSkipNextUpdate(x, y));
            }
            Assert.Equal(Coordinates(expected.GetProperty("active")), tiles.LiquidUpdates.CaptureActiveSnapshot().Select(c => (c.TileIndex / 100, c.TileIndex % 100)));
            Assert.Equal(Coordinates(expected.GetProperty("buffered")), tiles.LiquidUpdates.CaptureBufferSnapshot().Select(c => (c / 100, c % 100)));
            Assert.Equal(expected.GetProperty("network").GetArrayLength(), registry.PendingLiquidUpdates);
            Assert.Equal(point.GetProperty("during").EnumerateArray().Select(x => x.GetString()!), Drain(queue));
            var rng = expected.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Access)!.GetValue(random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Access)!.GetValue(random)!);
            while (registry.PendingLiquidUpdates != 0) registry.FlushPendingLiquids();
            Assert.Equal(point.GetProperty("legacyProjection").EnumerateArray().Select(x => x.GetString()!), Drain(queue));
        }
    }

    private static IEnumerable<(int X, int Y)> Coordinates(JsonElement data) => data.EnumerateArray().Select(c => (c.GetProperty("x").GetInt32(), c.GetProperty("y").GetInt32()));
    private static string[] Drain(TerrariaConnectionOutboundQueue queue) => GemBunnyWire1458Tests.Drain(queue).Select(Convert.ToHexString).ToArray();
}
