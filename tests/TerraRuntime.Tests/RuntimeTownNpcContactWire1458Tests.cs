using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcContactWire1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using Stream stream = typeof(RuntimeTownNpcContactWire1458Tests).Assembly.GetManifestResourceStream("TownContactWire1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument json = JsonDocument.Parse(gzip);
        foreach (JsonElement row in json.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    // Recorded at official ISocket.AsyncSend, including actual StrikeNPC and full UpdateNPC ordering.
    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Source_contact_strike_bytes_precede_the_single_final_npc_update(JsonElement row)
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var f = new RuntimeTownNpcContact1458Tests.Fixture(row.GetProperty("scenario").GetString()!,
            row.GetProperty("state").GetSingle(), row.GetProperty("seed").GetInt32(), replication);
        var queue = Endpoint(replication, 9010, 0); Drain(queue);
        byte[][] expected = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
        bool outer = row.GetProperty("outer").GetBoolean();
        if (outer) Assert.Equal(0, f.Tick().RejectedCommits);
        else
        {
            Span<NpcSnapshot> peers = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
            int count = f.Npcs.CopyActive(peers);
            var source = f.Before;
            var input = new NpcStateUpdate(source.Type, source.NetId, source.PositionX, source.PositionY,
                source.VelocityX, source.VelocityY, source.Target, source.Ai, source.Simulation);
            Assert.True(f.Combat.TryPlanContact(in source, in input, peers[..count], true, out _, out var strike));
            if (strike.HasValue) f.Combat.PublishContact(in source, strike.Value);
        }
        byte[][] actual = Drain(queue);
        byte[][] expectedDamage = expected.Where(x => x[2] == 28).ToArray();
        byte[][] actualDamage = actual.Where(x => x[2] == 28).ToArray();
        Assert.Equal(expectedDamage.Length, actualDamage.Length);
        for (int i = 0; i < expectedDamage.Length; i++) Assert.Equal(expectedDamage[i], actualDamage[i]);
        if (outer && expectedDamage.Length > 0)
        {
            Assert.Equal(new byte[] { 28, 23 }, actual.Select(x => x[2]).ToArray());
            Assert.Equal(new byte[] { 28, 23 }, expected.Select(x => x[2]).ToArray());
            Assert.Single(f.Sink.Commits);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
    }

    [Fact]
    public void Actual_world_contact_broadcasts28_then23_to_all_playing_peers_and_rejected_phase_is_silent()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var f = new RuntimeTownNpcContact1458Tests.Fixture("overlap", 0f, 1458, replication);
        var world = new ServerRuntimeState(npcs: f.Npcs, worldTiles: f.Tiles, townNpcs: f.Town,
            naturalSpawnRandom: f.Adapter, npcAiStepper: new RejectAi(), npcReplication: replication);
        var a = Endpoint(replication, 9011, 0); var b = Endpoint(replication, 9012, 1); Drain(a); Drain(b);
        world.Tick();
        byte[][] frames = Drain(a);
        Assert.Equal(new byte[] { 28, 23 }, frames.Select(x => x[2]).ToArray());
        byte[][] peer = Drain(b); Assert.Equal(frames.Length, peer.Length);
        for (int i = 0; i < frames.Length; i++) Assert.Equal(frames[i], peer[i]);
        Assert.True(f.Npcs.TryGet(f.Before.Handle, out var hit));
        var lowLife = new NpcStateUpdate(hit.Type, hit.NetId, 639, 440, 0, 0, 255, default,
            hit.Simulation with { Life = 1, HostileContactImmunity = 0 });
        Assert.True(f.Npcs.TryUpdate(hit.Handle, in lowLife, out _)); Drain(a); Drain(b);
        world.Tick(); Assert.Empty(Drain(a)); Assert.Empty(Drain(b));
    }

    private static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry registry, long id, byte slot)
    {
        var source = GameCommandSourceId.FromConnection(id);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        Assert.True(registry.TryRegister(source, queue));
        var handle = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(handle.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(handle, in spawn); return queue;
    }
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var frames = new List<byte[]>();
        var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (owned.TryRead(out OutboundFrame frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
    private sealed class RejectAi : INpcAiStateStepper
    { public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; } }
}
