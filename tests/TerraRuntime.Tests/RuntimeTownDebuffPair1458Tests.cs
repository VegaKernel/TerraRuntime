using System.IO.Compression;
using System.Buffers;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownDebuffPair1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(RuntimeTownDebuffPair1458Tests).Assembly.GetManifestResourceStream("TownDebuffPair1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        foreach (var row in doc.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_player_phase_and_two_outer_NPC_updates_match_expiry_emote_frames_and_next_rng(string json)
    {
        using var doc = JsonDocument.Parse(json); var row = doc.RootElement;
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { seed = row.GetProperty("seed").GetInt32(),
            slots = 0, life = 250, frame = row.GetProperty("frame").GetInt32() }));
        using var f = new RuntimeTownItemsSocial1458Tests.Fixture(input.RootElement, publishBuffs: true);
        f.Registry.BindNpcBuffStatus(f.Status);
        BuffTypeId[] playerBuffs = row.GetProperty("playerBuff").GetInt32() == 0 ? [] :
            [new(row.GetProperty("playerBuff").GetInt32())];
        f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Session.Slot, playerBuffs)));
        int held = row.GetProperty("held").GetInt32();
        f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
            new(f.Session.Slot, 0, (short)(held == 0 ? 0 : 1), 0, (short)held, 0)));
        f.Players.TickHealthContext();
        Assert.True(f.Players.TryGet(f.Connection, out var player));
        Assert.True(f.Players.TryGetInventoryItem(f.Connection, player.SelectedItem, out var heldItem));
        Assert.Equal(held, heldItem.ItemType.Value);
        Assert.Equal(new PlayerDebuffSnapshot1458(row.GetProperty("fire").GetBoolean(),
            row.GetProperty("fire2").GetBoolean(), row.GetProperty("poison").GetBoolean()), player.Debuffs);
        for (byte slot = 0; slot < 2; slot++)
        {
            var before = f.Current(slot);
            var state = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
                before.VelocityX, before.VelocityY, before.Target, before.Ai,
                before.Simulation with { LifeRegenCounter = -119 });
            Assert.True(f.Npcs.TryUpdate(before.Handle, in state, out var npc));
            Assert.True(f.Status.TryApply(npc.Handle, new(slot == 0 ? row.GetProperty("npcBuff").GetInt32() : 24), 1));
        }
        f.Status.BeginWorldTick(); f.Schedule.SetSocialContext(f.World, f.Players); Drain(f.Peer);
        f.Registry.AdvanceAuthoritativeTick();
        Assert.Equal(0, f.Schedule.Tick(in f.Conditions, [], f.Status, f.Combat).RejectedCommits);
        Assert.Equal(row.GetProperty("actorLife").GetInt32(), f.Current(0).Simulation.Life);
        Assert.Equal(row.GetProperty("peerLife").GetInt32(), f.Current(1).Simulation.Life);
        Assert.Equal(row.GetProperty("clock").GetDouble(), f.Current(0).Simulation.FrameCounter);
        Assert.Equal(row.GetProperty("peerClock").GetDouble(), f.Current(1).Simulation.FrameCounter);
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(x => x.GetString()),
            Drain(f.Peer).Select(Convert.ToHexString));
    }

    [Theory]
    [InlineData(20)] [InlineData(24)]
    public void Time_one_lethal_resident_damage_rejects_all_counters_slots_rng_and_frames(int buff)
    {
        using var input = JsonDocument.Parse("{\"seed\":16,\"slots\":0,\"life\":250,\"frame\":69}");
        using var f = new RuntimeTownItemsSocial1458Tests.Fixture(input.RootElement, publishBuffs: true);
        f.Registry.BindNpcBuffStatus(f.Status); var before = f.Current(0);
        var state = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai,
            before.Simulation with { Life = 1, LifeRegenCounter = -119 });
        Assert.True(f.Npcs.TryUpdate(before.Handle, in state, out before));
        Assert.True(f.Status.TryApply(before.Handle, new(buff), 1));
        f.Status.BeginWorldTick(); var expected = f.Random.Clone(); Drain(f.Peer);
        Assert.Equal(1, f.Schedule.Tick(in f.Conditions, [], f.Status, f.Combat).RejectedCommits);
        Assert.Equal(before, f.Current(0));
        Span<TerrariaNpcBuffEntryState> retained = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(f.Status.TryCopyWireBuffs(before.Handle, retained, out int count)); Assert.Equal(1, count);
        Assert.Equal(new TerrariaNpcBuffEntryState((ushort)buff, 1), retained[0]);
        Assert.DoesNotContain(Drain(f.Peer), x => x[2] is 54 or 91);
        Assert.True(f.Random.HasSameState(expected));
    }

    [Fact]
    public async Task Authenticated_53_crosses_real_queue_and_world_tick_before_source_expiry_and_peer_emote()
    {
        using var source = JsonDocument.Parse((string)Cases().First(x => {
            using var d = JsonDocument.Parse((string)x[0]); var r = d.RootElement;
            return r.GetProperty("npcBuff").GetInt32() == 20 && r.GetProperty("playerBuff").GetInt32() == 0 &&
                r.GetProperty("held").GetInt32() == 0 && r.GetProperty("frame").GetInt32() == 69;
        })[0]); var row = source.RootElement;
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { seed = row.GetProperty("seed").GetInt32(), slots = 0, life = 250, frame = 69 }));
        using var f = new RuntimeTownItemsSocial1458Tests.Fixture(input.RootElement); using var bootstrap = f.Bootstrap();
        for (byte slot = 0; slot < 2; slot++)
        {
            var before = f.Current(slot); var updated = new NpcStateUpdate(before.Type, before.NetId,
                before.PositionX, before.PositionY, before.VelocityX, before.VelocityY, before.Target, before.Ai,
                before.Simulation with { LifeRegenCounter = -119 });
            Assert.True(f.Npcs.TryUpdate(before.Handle, in updated, out _));
        }
        Drain(f.Peer);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int ticks = 0;
        using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(f.State,
            static (state, command) => state.Apply(command), state => {
                if (Interlocked.Increment(ref ticks) == 1) { state.Tick(); completed.TrySetResult(); }
            });
        var ingress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(loop);
        var sink = new NpcBuffFrameSink(f.Connection.Source, bootstrap, new ContinueSink(), new RuntimeNpcBuffNetworkIngress(ingress));
        for (short slot = 0; slot < 2; slot++)
        {
            var request = new TerrariaNpcBuffState(slot, (ushort)(slot == 0 ? 20 : 24), 1);
            Assert.True(TerrariaNpcBuffCodec.TryEncodeAdd(in request, out var encoded));
            var sequence = new ReadOnlySequence<byte>(encoded);
            Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out var frame));
            Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
        }
        Assert.Equal(250, f.Current(0).Simulation.Life); Assert.Empty(Drain(f.Peer));
        loop.Start(); await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(loop.Stop(TimeSpan.FromSeconds(5))); Assert.Null(loop.Fault);
        var observed = Drain(f.Peer);
        // Source AddBuff ingress publishes both full positive lists before the world phase empties them.
        Assert.Equal(new byte[] { 54, 54 }, observed.Take(2).Select(x => x[2]));
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(x => x.GetString()), observed.Skip(2).Select(Convert.ToHexString));
        Assert.Equal(249, f.Current(0).Simulation.Life); Assert.Equal(249, f.Current(1).Simulation.Life);
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
    }

    private sealed class ContinueSink : ITerrariaFrameSink
    { public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue; }

    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        var frames = new List<byte[]>(); while (inner.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray()); return frames.ToArray();
    }
}
