using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class GemBunnyWire1458Tests
{
    [Fact]
    public void Retained_birth_replays_source_ai7_home_before_the_first_update()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var npcs = new RuntimeNpcStore(commitSink: replication);
        Assert.True(npcs.TrySpawnVanillaPending(new(646, 646, 639f, 1580f, 0, 0, 255, default,
            NpcSimulationState.Initial), out var birth));
        var replay = Drain(Endpoint(replication, 9010, 0));
        Assert.Single(replay, frame => frame[2] == 23);
        // Original SendNPCHousesAndTravelShop includes every active aiStyle7, including
        // nonresidents whose constructor/default home has not yet been initialized by AI.
        Assert.Equal(Convert.FromHexString("0A003C0000FFFFFFFF00"), Assert.Single(replay, frame => frame[2] == 60));
        Assert.Equal(false, birth.Simulation.TownNpc);
    }

    [Fact]
    public void Original_socket_frames_cover_home60_force_cadence_contact_and_terminal_collision_body()
    {
        int phases = 0;
        foreach (string resource in new[] { "GemBunnyWireRuntimeLinux1458", "GemBunnyWireInteractionsLinux1458" })
        {
            using var stream = typeof(GemBunnyWire1458Tests).Assembly.GetManifestResourceStream(
                OperatingSystem.IsWindows() ? resource.Replace("Linux", "Windows", StringComparison.Ordinal) : resource)!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var json = JsonDocument.Parse(gzip);
            var rows = json.RootElement.EnumerateArray().Where(row =>
                (!row.TryGetProperty("outer", out var outer) || outer.GetBoolean()) && row.GetProperty("scene").GetString() != "catch");
            foreach (var group in rows.GroupBy(row => (Type: row.GetProperty("type").GetInt32(),
                         Scene: row.GetProperty("scene").GetString()!, Seed: row.TryGetProperty("seed", out var seed) ? seed.GetInt32() : 1458)))
            {
                var replication = new RuntimeNpcReplicationRegistry();
                var f = new GemBunnyLifecycle1458Tests.Fixture(group.Key.Type, group.Key.Scene, group.Key.Seed,
                    group.First().GetProperty("before"), replication);
                var first = Endpoint(replication, 9011, 0); var second = Endpoint(replication, 9012, 1);
                Drain(first); Drain(second);
                foreach (var row in group)
                {
                    f.Tick();
                    string[] expected = row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray();
                    string[] actual = Drain(first).Select(Convert.ToHexString).ToArray();
                    Assert.True(expected.SequenceEqual(actual), $"{group.Key} step{row.GetProperty("step")}: " +
                        $"expected {string.Join(';', expected)}, actual {string.Join(';', actual)}");
                    Assert.Equal(actual, Drain(second).Select(Convert.ToHexString).ToArray());
                    phases++;
                }
            }
        }
        Assert.Equal(1302, phases);
    }

    [Fact]
    public void A_retired_gem_home_cannot_reappear_during_late_join_or_slot_reuse()
    {
        using var stream = typeof(GemBunnyWire1458Tests).Assembly.GetManifestResourceStream("GemBunnyWireRuntimeLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        var row = json.RootElement.EnumerateArray().First(value => value.GetProperty("type").GetInt32() == 646 &&
            value.GetProperty("scene").GetString() == "birth" && value.GetProperty("outer").GetBoolean());
        var replication = new RuntimeNpcReplicationRegistry();
        var f = new GemBunnyLifecycle1458Tests.Fixture(646, "birth", 1458, row.GetProperty("before"), replication);
        f.Tick();
        var joined = Drain(Endpoint(replication, 9013, 2));
        Assert.Single(joined, frame => frame[2] == 23);
        byte[] home = Assert.Single(joined, frame => frame[2] == 60);
        Assert.Equal(Convert.FromHexString("0A003C00002800FFFF00"), home);
        Assert.True(f.Npcs.TryGet(f.Birth.Handle, out var source));
        Assert.True(f.Npcs.TryDespawn(source.Handle));
        Assert.Empty(Drain(Endpoint(replication, 9014, 3)));
        var replacement = new NpcStateUpdate(3, 3, source.PositionX, source.PositionY, 0, 0, 0, default,
            NpcSimulationState.Initial);
        Assert.True(f.Npcs.TrySpawn(0, in replacement, out var reused));
        Assert.NotEqual(source.Handle, reused.Handle);
        var current = Drain(Endpoint(replication, 9015, 4));
        Assert.Single(current, frame => frame[2] == 23);
        Assert.DoesNotContain(current, frame => frame[2] == 60);
        replication.NpcStateCommitted(NpcStateCommitKind.Update, in source);
        Assert.DoesNotContain(Drain(Endpoint(replication, 9016, 5)), frame => frame[2] == 60);
    }

    internal static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry registry, long id, byte slot)
    {
        var source = GameCommandSourceId.FromConnection(id);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(512, 262144, 1024));
        Assert.True(registry.TryRegister(source, queue));
        var handle = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(handle.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(handle, in spawn);
        return queue;
    }

    internal static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var frames = new List<byte[]>();
        var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (owned.TryRead(out OutboundFrame frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
}
