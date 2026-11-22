using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringTriggers1458Tests
{
    public static IEnumerable<object[]> OriginalLoadedKeys()
    {
        using var input = typeof(WiringTriggers1458Tests).Assembly.GetManifestResourceStream("WiringPressureLoadLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalLoadedKeys))]
    public void Saved_key_type_not_activity_controls_poke_and_empty_sensor_end_clears_global_block(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = CreateTiles(row.GetProperty("colors").GetInt32(), row.GetProperty("type").GetUInt16());
        var point = tiles.Get(30, 40); point.TrySetFlags(WorldTileFlags.Active, row.GetProperty("active").GetBoolean()); tiles.Set(30, 40, point);
        Assert.True(tiles.TryAttachPressurePlates([new(30, 40)], 16, fromWorldFile: true));
        var random = new VanillaUnifiedRandom1458(1458); var replication = new RuntimeTileManipulationReplicationRegistry();
        var queue = new TerrariaConnectionOutboundQueue(new(8192, 4_194_304, 8192));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(1458121), new(new PlayerSlotId(1), new(1)));
        Assert.True(replication.TryRegister(peer.Source, queue)); replication.PlayerSpawned(peer, new(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0));
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random));
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Hidden)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Hidden)!.GetValue(graph.WorldTileAuthority)!;
        random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));
        foreach (var step in row.GetProperty("points").EnumerateArray())
        {
            switch (step.GetProperty("op").GetString())
            {
                case "update": graph.WorldTileAuthority.TickWeightedPlates(); break;
                case "reset": graph.WorldTileAuthority.ReleaseWeightedPlayer(new(new PlayerSlotId(0), new(1))); break;
                case "sensor-end": graph.WorldTileAuthority.TickLogicSensors(); break;
                default: Assert.Fail("Unknown source step."); break;
            }
            Assert.Equal(step.GetProperty("blocked").GetBoolean(), circuit.PlayerTeleportationBlocked);
            Assert.Equal(step.GetProperty("mechanisms").GetInt32(), circuit.RegisteredMechanismCount);
            Assert.Equal(WiringLogicGate1458Tests.Decode(step.GetProperty("point")), tiles.Get(30, 40));
            Assert.Equal(WiringLogicGate1458Tests.Decode(step.GetProperty("torch")), tiles.Get(33, 40));
            Assert.Equal(step.GetProperty("pressed").EnumerateArray().Select(p => new WorldPressurePlate(p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32())), tiles.RuntimePressurePlates!.CaptureSnapshot());
            Assert.Equal(step.GetProperty("frames").EnumerateArray().Select(f => f.GetString()), GemBunnyWire1458Tests.Drain(queue).Select(Convert.ToHexString));
            AssertRandom(step.GetProperty("random"), random);
        }
    }
}
