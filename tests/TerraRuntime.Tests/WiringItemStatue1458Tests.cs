using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringItemStatue1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(WiringItemStatue1458Tests).Assembly.GetManifestResourceStream("WiringItemStatue1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Item_statue_geometry_caps_calendar_cooldown_allocation_and_packets_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100)); var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var replication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(replication);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(8192, 4194304, 8192));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1458105), new(new PlayerSlotId(1), new(1)));
        Assert.True(replication.TryRegister(connection.Source, queue));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0); replication.PlayerSpawned(connection, in spawn);
        int calendar = row.GetProperty("calendar").GetInt32();
        var state = new ServerRuntimeState(worldTiles: tiles, worldItems: items, worldItemReplication: replication,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
            { Halloween = (calendar & 1) != 0, XMas = (calendar & 2) != 0, TenthAnniversaryWorld = (calendar & 4) != 0 });
        const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", access)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", access)!.GetValue(graph.WorldTileAuthority)!;
        foreach (var cell in row.GetProperty("before").GetProperty("cells").EnumerateArray())
        {
            var tile = WiringLogicGate1458Tests.Decode(cell.GetProperty("tile"));
            tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), in tile);
        }
        foreach (var item in row.GetProperty("before").GetProperty("state").GetProperty("items").EnumerateArray())
        {
            var drop = new WorldItemDropStateUpdate(item.GetProperty("x").GetSingle(), item.GetProperty("y").GetSingle(),
                item.GetProperty("vx").GetSingle(), item.GetProperty("vy").GetSingle(), item.GetProperty("stack").GetInt16(),
                item.GetProperty("prefix").GetByte(), WorldItemOwnershipMode.None, item.GetProperty("type").GetInt16(), false, 0, 0);
            short slot;
            using (var allocation = items.CreateAllocationPreview())
            {
                Assert.True(allocation.TrySpawnSource(in drop, 0, out slot)); Assert.Equal(item.GetProperty("slot").GetInt16(), slot);
                Assert.True(allocation.TryClaim()); Assert.True(allocation.TryCommitNext(out _, out _));
            }
            // Controlled source preimages explicitly set physical age, independently
            // of owner-reservation age. No production write path is inferred here.
            var slots = (Array)typeof(RuntimeWorldItemStore).GetField("_slots", access)!.GetValue(items)!;
            var entry = slots.GetValue(slot)!;
            entry.GetType().GetField("SourceAge", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entry, item.GetProperty("age").GetInt32());
            slots.SetValue(entry, slot);
        }
        if (row.GetProperty("kind").GetString() == "mech-full")
            for (int x = 2; x < 78; x++) for (int y = 2; y < 98; y++)
                if ((x != 30 || y != 30) && circuit.RegisteredMechanismCount < 999) Assert.True(circuit.Register(x, y, 600));
        Drain(queue);
        int tick = 0; var scan = new WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            while (tick < point.GetProperty("tick").GetInt32()) { circuit.Tick(); tick++; }
            if (point.GetProperty("cleared").GetBoolean())
            {
                int count = items.CopyActive(scan);
                for (int i = 0; i < count; i++) Assert.True(items.TryRemove(scan[i].Handle.Slot, out _));
            }
            Drain(queue);
            Assert.True(circuit.HitSwitch(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32()));
            var expected = point.GetProperty("state");
            var expectedItems = expected.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("slot").GetInt32() < 400).ToArray();
            Assert.Equal(expectedItems.Length, items.CopyActive(scan));
            foreach (var item in expectedItems)
            {
                Assert.True(items.TryGetActive(item.GetProperty("slot").GetInt16(), out var actual));
                Assert.Equal(item.GetProperty("type").GetInt32(), actual.ItemNetId); Assert.Equal(item.GetProperty("stack").GetInt16(), actual.Stack);
                Assert.Equal(item.GetProperty("prefix").GetByte(), actual.Prefix);
                Assert.Equal(item.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(item.GetProperty("y").GetSingle(), actual.PositionY);
                Assert.Equal(item.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(item.GetProperty("vy").GetSingle(), actual.VelocityY);
                Assert.Equal(item.GetProperty("owner").GetByte(), actual.OwnerPlayerId); Assert.Equal(item.GetProperty("keep").GetInt32(), actual.TimeToKeepReservation);
                Assert.Equal(item.GetProperty("enemy").GetByte(), actual.EnemyGrabDelayTime);
                Assert.Equal(item.GetProperty("grabPlayer").GetByte(), actual.GrabDelayPlayer); Assert.Equal(item.GetProperty("grabTime").GetInt32(), actual.GrabDelayTime);
            }
            Assert.Equal(expected.GetProperty("mechs").GetInt32(), circuit.RegisteredMechanismCount);
            Assert.Equal(point.GetProperty("switchFrame").GetInt16(), tiles.Get(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32()).FrameY);
            var rng = expected.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", access)!.GetValue(random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", access)!.GetValue(random)!);
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!), Drain(queue));
        }
    }

    private static string[] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(queue)!;
        var frames = new List<string>(); while (inner.TryRead(out var frame)) frames.Add(Convert.ToHexString(frame.Bytes.Span));
        return frames.ToArray();
    }
}
