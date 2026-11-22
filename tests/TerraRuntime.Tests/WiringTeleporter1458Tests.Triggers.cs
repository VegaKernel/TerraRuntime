using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringTeleporter1458Tests
{
    public static IEnumerable<object[]> OriginalWeightedTeleportRows()
    {
        using var input = typeof(WiringTeleporter1458Tests).Assembly.GetManifestResourceStream("WiringTeleportTriggersLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            if (row.GetProperty("liquid").GetInt32() == 0)
            {
                yield return [row.GetRawText(), false];
                if (row.GetProperty("mount").GetInt32() == -1) yield return [row.GetRawText(), true];
            }
    }

    [Theory, MemberData(nameof(OriginalWeightedTeleportRows))]
    public void Weighted_entry_exit_nested_npc_teleports_global_flags_rng_and_packets_match_original(string observation, bool serverOwned)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100)); Assert.True(tiles.TryAttachWorldSurface(15));
        Assert.True(tiles.TryAttachPressurePlates([], 16));
        var random = new VanillaUnifiedRandom1458(1458);
        var npcReplication = new RuntimeNpcReplicationRegistry(); var npcs = new RuntimeNpcStore(commitSink: npcReplication);
        var registry = new RuntimeConnectionRegistry(); var tileReplication = new RuntimeTileManipulationReplicationRegistry();
        var queue = new TerrariaConnectionOutboundQueue(new(8192, 4_194_304, 8192));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(1458119), new(new PlayerSlotId(254), new(1)));
        Assert.True(npcReplication.TryRegister(peer.Source, queue)); Assert.True(registry.TryRegister(peer.Source, queue)); Assert.True(tileReplication.TryRegister(peer.Source, queue));
        var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        npcReplication.PlayerSpawned(peer, peerSpawn); registry.PlayerSpawned(peer, peerSpawn); tileReplication.PlayerSpawned(peer, peerSpawn);
        var slots = new PlayerSlotPool(2); var identities = new ServerPlayerSlotRegistry(slots); var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var owned = new ServerPlayerAuthority(states, identities, events: registry);
        var state = new ServerRuntimeState(playerEvents: registry, worldTiles: tiles, npcs: npcs, npcReplication: npcReplication,
            tileManipulationReplication: tileReplication, serverPlayers: serverOwned ? owned : null,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random));
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Hidden)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Hidden)!.GetValue(graph.WorldTileAuthority)!;
        foreach (var cell in row.GetProperty("before").GetProperty("cells").EnumerateArray())
        {
            var tile = WiringLogicGate1458Tests.Decode(cell.GetProperty("tile"));
            tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), tile);
        }
        var import = typeof(WiringNpcStatue1458Tests).GetMethod("Import", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var npc in row.GetProperty("before").GetProperty("npcs").EnumerateArray())
        {
            var update = (NpcStateUpdate)import.Invoke(null, [npc])!;
            Assert.True(npcs.TrySpawn(npc.GetProperty("slot").GetByte(), update, out var imported));
            Assert.True(npcs.TryUpdate(imported.Handle, update, out _));
        }
        var player = row.GetProperty("before").GetProperty("players")[0];
        float x = player.GetProperty("x").GetSingle(), y = player.GetProperty("y").GetSingle();
        float vx = player.GetProperty("vx").GetSingle(), vy = player.GetProperty("vy").GetSingle();
        PlayerJoinSession? session = null;
        if (serverOwned)
        {
            var actor = owned.Create(new("test:weighted-teleport"), x, y); Assert.True(actor.IsCreated);
            Assert.True(states.TrySetMotion(actor.Player, x, y, vx, vy, out _));
        }
        else
        {
            Assert.True(slots.TryAcquireConnection(out var lease)); session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1458120), session.Handle);
            Assert.True(registry.TryRegister(connection.Source, new TerrariaConnectionOutboundQueue(new(8192, 4_194_304, 8192))));
            state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 20, 20, 0, 0, 0, 0, 0)));
            Assert.True(graph.Players.TryGet(connection, out var member)); Assert.True(member.TryAdvanceRevision());
            member.PositionX = x; member.PositionY = y; member.VelocityX = vx; member.VelocityY = vy;
            int mount = row.GetProperty("mount").GetInt32(); member.HasMount = mount >= 0; member.MountType = (ushort)Math.Max(0, mount);
        }
        try
        {
            random.CopyStateFrom(new VanillaUnifiedRandom1458(1458)); Drain(queue);
            foreach (var point in row.GetProperty("points").EnumerateArray())
            {
                Assert.True(circuit.HitSwitch(10, 40));
                Assert.True(graph.PlayerSnapshots.TryGetPlayer(new PlayerSlotId(0), out var actual));
                var expected = point.GetProperty("players")[0];
                Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
                Assert.Equal(point.GetProperty("flags").GetProperty("mountActive").GetBoolean(), actual.HasMount);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
                var history = serverOwned ? owned.CaptureWiringTeleport(actual.Player) : graph.Players.CaptureWiringTeleport(actual.Player);
                Assert.Equal(expected.GetProperty("teleportTime").GetSingle(), history.Time);
                Assert.Equal(expected.GetProperty("fallStart").GetInt32(), history.FallStart);
                Assert.Equal(expected.GetProperty("environmentImmunity").GetInt32(), history.EnvironmentImmunity);
                Assert.Equal(point.GetProperty("pressure").EnumerateArray().Select(p => new WorldPressurePlate(p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32())), tiles.RuntimePressurePlates!.CaptureSnapshot());
                foreach (var expectedNpc in point.GetProperty("npcs").EnumerateArray())
                {
                    Assert.True(npcs.TryGetActive(expectedNpc.GetProperty("slot").GetByte(), out var npc));
                    Assert.Equal(expectedNpc.GetProperty("x").GetSingle(), npc.PositionX); Assert.Equal(expectedNpc.GetProperty("y").GetSingle(), npc.PositionY);
                    Assert.Equal(expectedNpc.GetProperty("teleportTime").GetSingle(), npc.Simulation.TeleportTime);
                }
                AssertRandom(point.GetProperty("random"), random);
                Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(f => f.GetString()), Drain(queue));
            }
        }
        finally { session?.Dispose(); }
    }
}
