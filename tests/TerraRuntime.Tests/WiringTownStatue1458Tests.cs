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
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringTownStatue1458Tests
{
    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic;
    public static IEnumerable<object[]> OriginalVisuals()
    {
        using var input = typeof(WiringTownStatue1458Tests).Assembly.GetManifestResourceStream("WiringTownStatue1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("visuals").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalVisuals))]
    public void Style14_history_decay_matches_original_without_particle_entropy(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        Assert.Equal(row.GetProperty("beforeRandom").GetRawText(), row.GetProperty("afterRandom").GetRawText());
        float time = row.GetProperty("initial").GetSingle();
        foreach (var expected in row.GetProperty("times").EnumerateArray())
        {
            time = VanillaTownStatue1458.AdvanceTeleportTime(time);
            Assert.Equal(expected.GetSingle(), time);
        }
        Assert.Equal(0f, time);
    }
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(WiringTownStatue1458Tests).Assembly.GetManifestResourceStream("WiringTownStatue1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Town_statue_selection_cooldown_state_rng_and_packet65_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100)); var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var registry = new RuntimeNpcReplicationRegistry(); var npcs = new RuntimeNpcStore(commitSink: registry);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(8192, 4194304, 8192));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(145840), new(new PlayerSlotId(1), new(1)));
        Assert.True(registry.TryRegister(connection.Source, queue));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0); registry.PlayerSpawned(connection, in spawn);
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, npcReplication: registry,
            naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Access)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Access)!.GetValue(graph.WorldTileAuthority)!;
        int style = row.GetProperty("style").GetInt32(), colors = row.GetProperty("colors").GetInt32();
        WorldTileFlags wires = 0;
        if ((colors & 1) != 0) wires |= WorldTileFlags.WireRed;
        if ((colors & 2) != 0) wires |= WorldTileFlags.WireBlue;
        if ((colors & 4) != 0) wires |= WorldTileFlags.WireGreen;
        if ((colors & 8) != 0) wires |= WorldTileFlags.WireYellow;
        int ox = row.GetProperty("originX").GetInt32(), oy = row.GetProperty("originY").GetInt32(), contactX = 30 + row.GetProperty("contact").GetInt32() / 3;
        for (int x = Math.Min(ox, contactX); x <= Math.Max(ox, contactX); x++) tiles.Set(x, oy, new WorldTile { Flags = wires });
        tiles.Set(ox, oy, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | wires });
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
            tiles.Set(30 + dx, 30 + dy, new WorldTile { Type = 105, FrameX = (short)(style * 36 + dx * 18), FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | wires });
        foreach (var n in row.GetProperty("before").GetProperty("npcs").EnumerateArray())
        {
            var update = Import(n); Assert.True(npcs.TrySpawn(n.GetProperty("slot").GetByte(), in update, out var imported));
            Assert.True(npcs.TryUpdate(imported.Handle, in update, out _));
            if (row.GetProperty("pending").GetBoolean())
            {
                // Trusted original-stock preimage, not a production writer.
                var slots = (Array)typeof(RuntimeNpcStore).GetField("_slots", Access)!.GetValue(npcs)!;
                var slot = slots.GetValue(imported.Handle.Slot)!; slot.GetType().GetField("BirthPending")!.SetValue(slot, true); slots.SetValue(slot, imported.Handle.Slot);
            }
        }
        Drain(queue); int tick = 0;
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            while (tick < point.GetProperty("tick").GetInt32()) { circuit.Tick(); tick++; }
            var preimages = new NpcSnapshot[200]; int count = npcs.CopyActive(preimages);
            Assert.True(circuit.HitSwitch(ox, oy)); var expected = point.GetProperty("state");
            Assert.Equal(expected.GetProperty("npcs").GetArrayLength(), npcs.ActiveCount);
            foreach (var n in expected.GetProperty("npcs").EnumerateArray())
            {
                Assert.True(npcs.TryGetActive(n.GetProperty("slot").GetByte(), out var actual));
                Assert.Equal(n.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(n.GetProperty("y").GetSingle(), actual.PositionY);
                Assert.Equal(n.GetProperty("teleportTime").GetSingle(), actual.Simulation.TeleportTime);
                Assert.Equal(n.GetProperty("teleportStyle").GetInt32(), actual.Simulation.TeleportStyle);
                Assert.Equal(n.GetProperty("pending").GetBoolean(), npcs.HasPendingBirth(actual.Handle));
                var before = preimages.Take(count).Single(x => x.Handle == actual.Handle);
                Assert.Equal(before, actual with { Revision = before.Revision, PositionX = before.PositionX, PositionY = before.PositionY,
                    Simulation = actual.Simulation with { TeleportTime = before.Simulation.TeleportTime, TeleportStyle = before.Simulation.TeleportStyle } });
            }
            Assert.Equal(expected.GetProperty("mechs").GetInt32(), circuit.RegisteredMechanismCount);
            var rng = expected.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Access)!.GetValue(random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Access)!.GetValue(random)!);
            Assert.Equal(point.GetProperty("during").EnumerateArray().Select(x => x.GetString()!), Drain(queue));
            npcs.PublishPendingBirths(); Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!), Drain(queue));
            // A new playing endpoint receives the retained post-teleport baseline,
            // captured with original SendData(23), pending=true for the existing
            // runtime reset policy. This is not an original joining-session trace.
            var joinQueue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(8192, 4194304, 8192));
            var joiner = new ConnectionHandle(GameCommandSourceId.FromConnection(145841), new(new PlayerSlotId(2), new(1)));
            Assert.True(registry.TryRegister(joiner.Source, joinQueue));
            var joiningSpawn = spawn with { ClaimedSlot = joiner.Player.Slot }; registry.PlayerSpawned(joiner, in joiningSpawn);
            var joinFrames = Drain(joinQueue);
            Assert.All(joinFrames, encoded => Assert.Contains(Convert.FromHexString(encoded)[2], new byte[] { 23, 54, 60 }));
            Assert.Equal(point.GetProperty("baseline").EnumerateArray().Select(x => x.GetString()!), joinFrames.Where(encoded => Convert.FromHexString(encoded)[2] == 23));
            Assert.True(registry.TryUnregister(joiner.Source));
        }
    }

    private static NpcStateUpdate Import(JsonElement n) => new(n.GetProperty("type").GetInt32(), n.GetProperty("netId").GetInt16(),
        n.GetProperty("x").GetSingle(), n.GetProperty("y").GetSingle(), n.GetProperty("vx").GetSingle(), n.GetProperty("vy").GetSingle(),
        n.GetProperty("target").GetUInt16(), default, NpcSimulationState.Initial with
        {
            TeleportTime = n.GetProperty("teleportTime").GetSingle(), TeleportStyle = n.GetProperty("teleportStyle").GetInt32(),
            OldPositionX = n.GetProperty("oldX").GetSingle(), OldPositionY = n.GetProperty("oldY").GetSingle(),
            Life = n.GetProperty("life").GetInt32(), LifeMax = n.GetProperty("lifeMax").GetInt32(),
            BaseDamage = n.GetProperty("damage").GetInt32(), BaseDefense = n.GetProperty("defense").GetInt32(),
            HitboxOverride = new(n.GetProperty("width").GetInt32(), n.GetProperty("height").GetInt32()),
            Scale = n.GetProperty("scale").GetSingle(), MoneyValue = n.GetProperty("value").GetSingle(), SpawnSlotCost = n.GetProperty("slots").GetSingle(),
            SpawnedFromStatue = n.GetProperty("statue").GetBoolean(), CanBeReplacedByOtherNpcs = n.GetProperty("replace").GetBoolean(),
            TimeLeft = n.GetProperty("timeLeft").GetInt32(), DirectionX = n.GetProperty("direction").GetInt32(), DirectionY = n.GetProperty("directionY").GetInt32(),
            SpriteDirection = n.GetProperty("sprite").GetInt32(), Friendly = n.GetProperty("friendly").GetBoolean(), Wet = n.GetProperty("wet").GetBoolean(),
            NoGravity = n.GetProperty("noGravity").GetBoolean(), NoTileCollide = n.GetProperty("noTileCollide").GetBoolean(), Alpha = n.GetProperty("alpha").GetInt32(),
            ReleaseOwner = n.GetProperty("releaseOwner").GetInt16(), CatchableNpcTempImmunityCounter = n.GetProperty("immunity").GetInt32()
        });

    private static string[] Drain(TerrariaConnectionOutboundQueue queue) => GemBunnyWire1458Tests.Drain(queue).Select(Convert.ToHexString).ToArray();
}
