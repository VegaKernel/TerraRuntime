using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.HostContracts;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringTeleporter1458Tests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(WiringTeleporter1458Tests).Assembly.GetManifestResourceStream("WiringTeleporterLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
        {
            yield return [row.GetRawText(), false];
            if (row.GetProperty("actor").GetInt32() != 0) yield return [row.GetRawText(), true];
        }
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Ordered_pairs_actor_motion_rng_and_literal65_match_official_server(string observation, bool serverOwned)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100)); Assert.True(tiles.TryAttachWorldSurface(15));
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var replication = new RuntimeNpcReplicationRegistry(); var npcs = new RuntimeNpcStore(commitSink: replication);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(8192, 4194304, 8192));
        var registry = new RuntimeConnectionRegistry();
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(1458105), new(new PlayerSlotId(254), new(1)));
        Assert.True(replication.TryRegister(peer.Source, queue)); Assert.True(registry.TryRegister(peer.Source, queue));
        var spawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(peer, in spawn); registry.PlayerSpawned(peer, in spawn);
        var slots = new PlayerSlotPool(2); var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var owned = new ServerPlayerAuthority(states, identities, events: registry);
        var state = new ServerRuntimeState(playerEvents: registry, npcs: npcs, worldTiles: tiles, npcReplication: replication,
            serverPlayers: serverOwned ? owned : null,
            naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { DownedPlantera = row.GetProperty("kind").GetString() == "temple-downed" });
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Hidden)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Hidden)!.GetValue(graph.WorldTileAuthority)!;
        foreach (var cell in row.GetProperty("before").GetProperty("cells").EnumerateArray())
        {
            var image = cell.GetProperty("tile"); var tile = WiringLogicGate1458Tests.Decode(image);
            tile.Wall = image.GetProperty("wall").GetUInt16();
            Assert.True(tile.TrySetSourceShape(image.GetProperty("halfBrick").GetBoolean(), image.GetProperty("slope").GetByte()));
            tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), in tile);
        }
        var import = typeof(WiringNpcStatue1458Tests).GetMethod("Import", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var npc in row.GetProperty("before").GetProperty("npcs").EnumerateArray())
        {
            var update = (NpcStateUpdate)import.Invoke(null, [npc])!;
            update = update with { Simulation = update.Simulation with { TeleportTime = npc.GetProperty("teleportTime").GetSingle(), TeleportStyle = npc.GetProperty("teleportStyle").GetInt32() } };
            Assert.True(npcs.TrySpawn(npc.GetProperty("slot").GetByte(), in update, out var imported));
            Assert.True(npcs.TryUpdate(imported.Handle, in update, out _));
        }
        var sessions = new List<PlayerJoinSession>();
        foreach (var p in row.GetProperty("before").GetProperty("players").EnumerateArray())
        {
            float x = p.GetProperty("x").GetSingle(), y = p.GetProperty("y").GetSingle(), vx = p.GetProperty("vx").GetSingle(), vy = p.GetProperty("vy").GetSingle();
            if (serverOwned)
            {
                var result = owned.Create(new ServerPlayerId($"test:teleporter-{p.GetProperty("slot").GetInt32()}"), x, y);
                Assert.True(result.IsCreated); Assert.Equal(p.GetProperty("slot").GetByte(), result.Player.Slot.Value);
                Assert.True(states.TrySetMotion(result.Player, x, y, vx, vy, out _));
            }
            else
            {
                Assert.True(slots.TryAcquireConnection(out var lease));
                var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease)); sessions.Add(session);
                session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(2000 + p.GetProperty("slot").GetInt32()), session.Handle);
                Assert.True(registry.TryRegister(connection.Source, new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(8192, 4194304, 8192))));
                state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 20, 20, 0, 0, 0, 0, 0)));
                // Import the official synthetic midstate; its y lies inside the normal vanilla border band.
                Assert.True(graph.Players.TryGet(connection, out var imported));
                Assert.True(imported.TryAdvanceRevision());
                imported.PositionX = x; imported.PositionY = y; imported.VelocityX = vx; imported.VelocityY = vy;
            }
        }
        random.CopyStateFrom(new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32()));
        Drain(queue);
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            if (row.GetProperty("kind").GetString() == "block") Assert.True(circuit.TripWireWithoutPlayerTeleportation(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32(), 1, 1));
            else Assert.True(circuit.HitSwitch(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32()));
            foreach (var n in point.GetProperty("npcs").EnumerateArray())
            {
                Assert.True(npcs.TryGetActive(n.GetProperty("slot").GetByte(), out var actual));
                typeof(WiringNpcStatue1458Tests).GetMethod("AssertNpc", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [n, actual, npcs]);
                Assert.Equal(n.GetProperty("teleportTime").GetSingle(), actual.Simulation.TeleportTime);
                Assert.Equal(n.GetProperty("teleportStyle").GetInt32(), actual.Simulation.TeleportStyle);
            }
            foreach (var p in point.GetProperty("players").EnumerateArray())
            {
                PlayerStateSnapshot actual;
                if (serverOwned) Assert.True(owned.TryGet(new PlayerSlotId(p.GetProperty("slot").GetByte()), out actual));
                else { Assert.True(graph.Players.TryGet(new PlayerSlotId(p.GetProperty("slot").GetByte()), out var member)); actual = member.CaptureSnapshot(); }
                Assert.Equal(p.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(p.GetProperty("y").GetSingle(), actual.PositionY);
                Assert.Equal(p.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(p.GetProperty("vy").GetSingle(), actual.VelocityY);
                var history = serverOwned ? owned.CaptureWiringTeleport(actual.Player) : graph.Players.CaptureWiringTeleport(actual.Player);
                Assert.Equal(p.GetProperty("teleportTime").GetSingle(), history.Time);
                if (history.Owner.IsAssigned)
                {
                    Assert.Equal(p.GetProperty("fallStart").GetInt32(), history.FallStart);
                    Assert.Equal(p.GetProperty("environmentImmunity").GetInt32(), history.EnvironmentImmunity);
                }
            }
            AssertRandom(point.GetProperty("random"), random);
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!), Drain(queue));
            foreach (var cell in point.GetProperty("cells").EnumerateArray())
            {
                var image = cell.GetProperty("tile"); var actual = tiles.Get(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32());
                Assert.Equal(image.GetProperty("frameX").GetInt16(), actual.FrameX); Assert.Equal(image.GetProperty("frameY").GetInt16(), actual.FrameY);
            }
        }
        foreach (var session in sessions) session.Dispose();
    }

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Hidden)!.GetValue(random)));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Hidden)!.GetValue(random)!);
    }
    private static string[] Drain(TerrariaConnectionOutboundQueue queue) => GemBunnyWire1458Tests.Drain(queue).Select(Convert.ToHexString).ToArray();
}
