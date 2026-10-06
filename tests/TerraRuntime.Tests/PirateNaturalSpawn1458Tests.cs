using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class PirateNaturalSpawn1458Tests
{
    [Fact]
    public void Original_fixed_floor_Pirate_selection_preserves_half_boundary_caps_and_shared_cursor()
    {
        using var json = Source();
        int checkedRows = 0;
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
        {
            var random = new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
            var invasion = State(row);
            int terrainReads = 0;
            var selected = RuntimeInvasionSpawn1458.SelectPirate(in invasion,
                row.GetProperty("ship").GetBoolean(), row.GetProperty("captain").GetBoolean(),
                () => { terrainReads++; return Blocked(row); }, random);
            Assert.Equal(row.GetProperty("type").GetInt32(), selected.Value);
            Assert.Equal(row.GetProperty("next").GetInt32(), random.NextInt32(0, int.MaxValue));
            Assert.InRange(terrainReads, 0, 1);
            if (invasion.Size >= invasion.SizeStart / 2 || row.GetProperty("ship").GetBoolean())
                Assert.Equal(0, terrainReads);
            checkedRows++;
        }
        Assert.Equal(19, checkedRows);
    }

    [Fact]
    public void Original_ordinary_Pirate_births_keep_zero_ai_source_body_and_literal_packet23()
    {
        using var json = Source();
        int checkedRows = 0;
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("type").GetInt32() == 491)
                continue; // Linked ship lifecycle is explicitly not admitted by this component.
            var registry = new RuntimeNpcReplicationRegistry();
            var queue = Endpoint(registry);
            var store = new RuntimeNpcStore(commitSink: registry);
            store.SetVanillaSpawnContextSource(() => new(1f, 1, false));
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
            Assert.True(store.TryCreateNaturalSpawnPreview(random, out var preview));
            var invasion = State(row);
            var type = RuntimeInvasionSpawn1458.SelectPirate(in invasion,
                row.GetProperty("ship").GetBoolean(), row.GetProperty("captain").GetBoolean(),
                () => Blocked(row), new SystemVanillaNpcRandom(preview!.Random));
            var update = new NpcStateUpdate(type.Value, checked((short)type.Value), 0f, 0f, 0f, 0f,
                0, default, NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
            Assert.True(preview.TryStage(in update, 808f, row.GetProperty("spawnY").GetInt32() * 16f, out var staged));
            Assert.Equal(0, store.ActiveCount);
            Assert.Empty(Drain(queue));
            Assert.True(preview.ValidateContext());
            Assert.True(preview.TryAdoptOwned(out var accepted));
            var born = Assert.IsType<NpcSnapshot>(accepted);
            Assert.Equal(staged, born);
            Assert.Equal(row.GetProperty("type").GetInt32(), born.Type);
            Assert.Equal(row.GetProperty("x").GetSingle(), born.PositionX);
            Assert.Equal(row.GetProperty("y").GetSingle(), born.PositionY);
            Assert.Equal(row.GetProperty("life").GetInt32(), born.Simulation.Life);
            Assert.Equal(row.GetProperty("lifeMax").GetInt32(), born.Simulation.LifeMax);
            Assert.False(born.Simulation.TownNpc);
            Assert.Equal(default, born.Ai);
            Assert.Empty(Drain(queue));
            store.PublishPendingBirths();
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(v => v.GetString()).ToArray(),
                Drain(queue).Select(Convert.ToHexString).ToArray());
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
            checkedRows++;
        }
        Assert.Equal(14, checkedRows);
    }

    private static InvasionState1458 State(JsonElement row) => default(InvasionState1458) with
    {
        Type = 3, Size = row.GetProperty("size").GetInt32(),
        SizeStart = row.GetProperty("sizeStart").GetInt32(), X = 50
    };

    private static bool Blocked(JsonElement row) => row.GetProperty("terrain").GetString() == "solid" ||
        row.GetProperty("spawnY").GetInt32() < 40;

    private static JsonDocument Source()
    {
        using var stream = typeof(PirateNaturalSpawn1458Tests).Assembly.GetManifestResourceStream("InvasionPirateBirth1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry registry)
    {
        var source = GameCommandSourceId.FromConnection(9971);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        Assert.True(registry.TryRegister(source, queue));
        var connection = new ConnectionHandle(source, new(new(1), new(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(connection, in spawn);
        return queue;
    }

    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var frames = new List<byte[]>();
        var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (owned.TryRead(out OutboundFrame frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
}
