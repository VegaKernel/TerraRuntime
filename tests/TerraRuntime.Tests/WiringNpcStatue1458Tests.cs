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

public sealed partial class WiringNpcStatue1458Tests
{
    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic;
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(WiringNpcStatue1458Tests).Assembly.GetManifestResourceStream(OperatingSystem.IsWindows() ? "WiringNpcStatueWindows1458" : "WiringNpcStatueLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Statue_birth_population_replacement_pending_wire_and_rng_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100)); var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var registry = new RuntimeNpcReplicationRegistry(); var npcs = new RuntimeNpcStore(commitSink: registry);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(8192, 4194304, 8192));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1458105), new(new PlayerSlotId(1), new(1)));
        Assert.True(registry.TryRegister(connection.Source, queue));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0); registry.PlayerSpawned(connection, in spawn);
        int mode = row.GetProperty("mode").GetInt32(), mask = row.GetProperty("world").GetInt32();
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, npcReplication: registry,
            expertMode: mode > 0, masterMode: mode == 2, worldClock: new RuntimeWorldClock(1000, true, default, 0, 0, getGoodWorld: row.GetProperty("good").GetBoolean()),
            naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { PartyIsUp = row.GetProperty("party").GetBoolean(), GameMode = (byte)mode, HardMode = (mask & 1) != 0, RemixWorld = (mask & 2) != 0,
                TenthAnniversaryWorld = (mask & 4) != 0, DownedPlantera = (mask & 8) != 0 });
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Access)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Access)!.GetValue(graph.WorldTileAuthority)!;
        foreach (var cell in row.GetProperty("before").GetProperty("cells").EnumerateArray())
        {
            var tile = WiringLogicGate1458Tests.Decode(cell.GetProperty("tile"));
            tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), in tile);
        }
        foreach (var npc in row.GetProperty("before").GetProperty("state").GetProperty("npcs").EnumerateArray())
        {
            var update = Import(npc); Assert.True(npcs.TrySpawn(npc.GetProperty("slot").GetByte(), in update, out var imported));
            Assert.True(npcs.TryUpdate(imported.Handle, in update, out _));
        }
        if (row.GetProperty("kind").GetString() == "protected")
        {
            var slots = (Array)typeof(RuntimeNpcStore).GetField("_slots", Access)!.GetValue(npcs)!;
            var slot = slots.GetValue(0)!; slot.GetType().GetField("SpawnProtection", BindingFlags.Public | BindingFlags.Instance)!.SetValue(slot, 2); slots.SetValue(slot, 0);
        }
        if (row.GetProperty("kind").GetString() == "mech-full")
            for (int x = 2; x < 78; x++) for (int y = 2; y < 98; y++)
                if ((x != 30 || y != 30) && circuit.RegisteredMechanismCount < 999) Assert.True(circuit.Register(x, y, 30));
        Drain(queue); int tick = 0; var scan = new NpcSnapshot[256];
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            while (tick < point.GetProperty("tick").GetInt32()) { circuit.Tick(); tick++; }
            if (point.GetProperty("cleared").GetBoolean())
            {
                int count = npcs.CopyActive(scan); for (int i = 0; i < count; i++) Assert.True(npcs.TryDespawn(scan[i].Handle));
            }
            Drain(queue); Assert.True(circuit.HitSwitch(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32()));
            var expected = point.GetProperty("state");
            Assert.Equal(expected.GetProperty("npcs").GetArrayLength(), npcs.ActiveCount);
            foreach (var npc in expected.GetProperty("npcs").EnumerateArray())
            {
                Assert.True(npcs.TryGetActive(npc.GetProperty("slot").GetByte(), out var actual)); AssertNpc(npc, actual, npcs);
            }
            Assert.Equal(expected.GetProperty("mechs").GetInt32(), circuit.RegisteredMechanismCount);
            Assert.Equal(point.GetProperty("switchFrame").GetInt16(), tiles.Get(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32()).FrameY);
            var rng = expected.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Access)!.GetValue(random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Access)!.GetValue(random)!);
            Assert.Equal(point.GetProperty("during").EnumerateArray().Select(x => x.GetString()!), Drain(queue));
            npcs.PublishPendingBirths();
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!), Drain(queue));
        }
    }

    private static NpcStateUpdate Import(JsonElement n) => new(n.GetProperty("type").GetInt32(), n.GetProperty("netId").GetInt16(),
        n.GetProperty("x").GetSingle(), n.GetProperty("y").GetSingle(), n.GetProperty("vx").GetSingle(), n.GetProperty("vy").GetSingle(),
        n.GetProperty("target").GetUInt16(), default, NpcSimulationState.Initial with
        {
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

    private static void AssertNpc(JsonElement n, NpcSnapshot a, RuntimeNpcStore owner)
    {
        var s = a.Simulation; Assert.Equal(n.GetProperty("type").GetInt32(), a.Type); Assert.Equal(n.GetProperty("netId").GetInt16(), a.NetId);
        Assert.Equal(n.GetProperty("x").GetSingle(), a.PositionX); Assert.Equal(n.GetProperty("y").GetSingle(), a.PositionY);
        Assert.Equal(n.GetProperty("vx").GetSingle(), a.VelocityX); Assert.Equal(n.GetProperty("vy").GetSingle(), a.VelocityY);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(a.TypeIdentity, a.NetIdentity, out var definition)); Assert.True(definition.TryResolveHitbox(s, out var body));
        Assert.Equal(n.GetProperty("width").GetInt32(), body.Width); Assert.Equal(n.GetProperty("height").GetInt32(), body.Height);
        Assert.Equal(n.GetProperty("life").GetInt32(), s.Life); Assert.Equal(n.GetProperty("lifeMax").GetInt32(), s.LifeMax);
        Assert.Equal(n.GetProperty("damage").GetInt32(), s.DamageOverride ?? s.BaseDamage); Assert.Equal(n.GetProperty("defense").GetInt32(), s.DefenseOverride ?? s.BaseDefense);
        Assert.Equal(n.GetProperty("scale").GetSingle(), s.Scale); Assert.Equal(n.GetProperty("value").GetSingle(), s.MoneyValue);
        if (n.GetProperty("statue").GetBoolean()) Assert.Equal(n.GetProperty("slots").GetSingle(), s.SpawnSlotCost);
        Assert.Equal(n.GetProperty("statue").GetBoolean(), s.SpawnedFromStatue); Assert.Equal(n.GetProperty("replace").GetBoolean(), s.CanBeReplacedByOtherNpcs);
        Assert.Equal(n.GetProperty("pending").GetBoolean(), owner.HasPendingBirth(a.Handle)); Assert.Equal(n.GetProperty("generation").GetUInt64(), a.Handle.Generation.Value);
        Assert.Equal(n.GetProperty("timeLeft").GetInt32(), s.TimeLeft); Assert.Equal(n.GetProperty("direction").GetInt32(), s.DirectionX); Assert.Equal(n.GetProperty("directionY").GetInt32(), s.DirectionY);
        Assert.Equal(n.GetProperty("sprite").GetInt32(), s.SpriteDirection); Assert.Equal(n.GetProperty("target").GetUInt16(), a.Target);
        Assert.Equal(n.GetProperty("friendly").GetBoolean(), s.Friendly); Assert.Equal(n.GetProperty("wet").GetBoolean(), s.Wet);
        Assert.Equal(n.GetProperty("noGravity").GetBoolean(), s.NoGravity); Assert.Equal(n.GetProperty("noTileCollide").GetBoolean(), s.NoTileCollide); Assert.Equal(n.GetProperty("alpha").GetInt32(), s.Alpha);
        Assert.Equal(n.GetProperty("ai").EnumerateArray().Select(v => v.GetSingle()), new[] { a.Ai.Ai0, a.Ai.Ai1, a.Ai.Ai2, a.Ai.Ai3 });
        Assert.Equal(n.GetProperty("localAi").EnumerateArray().Select(v => v.GetSingle()), new[] { s.LocalAi.Ai0, s.LocalAi.Ai1, s.LocalAi.Ai2, s.LocalAi.Ai3 });
        if (s.ReleaseOwner is not null) Assert.Equal(n.GetProperty("releaseOwner").GetInt16(), s.ReleaseOwner);
        if (s.CatchableNpcTempImmunityCounter is not null) Assert.Equal(n.GetProperty("immunity").GetInt32(), s.CatchableNpcTempImmunityCounter);
    }
    private static string[] Drain(TerrariaConnectionOutboundQueue queue) => GemBunnyWire1458Tests.Drain(queue).Select(Convert.ToHexString).ToArray();
}
