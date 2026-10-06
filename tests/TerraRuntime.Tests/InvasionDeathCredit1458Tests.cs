using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed partial class InvasionDeathCredit1458Tests
{
    private static InvasionState1458 State(int type) => new(type, 12, 120, 0, 0, 0, 0, 0, 0, 0,
        false, false, false, false);

    [Fact]
    public void Actual_checkDead_credit_and_packet_follow_all_source_loot_and_heals()
    {
        using var stream = typeof(InvasionDeathCredit1458Tests).Assembly.GetManifestResourceStream("InvasionDeathOrder1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        int rows = 0;
        bool sawHealing = false;
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var sourceFrames = row.GetProperty("frames").EnumerateArray().Select(v => Convert.FromHexString(v.GetString()!)).ToArray();
            var sourceActive = row.GetProperty("activeAtSend").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int type = row.GetProperty("type").GetInt32();
            var sink = new Sink();
            var store = new RuntimeNpcStore(2, sink);
            var update = new NpcStateUpdate(type, checked((short)type), 800, 440, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 0, LifeMax = 100 });
            Assert.True(store.TrySpawn(0, in update, out var dead));
            sink.Events.Clear();
            var owner = new RuntimeWorldInvasion1458(State(row.GetProperty("group").GetInt32()));
            Assert.True(RuntimeNpcInvasionDeathCredit1458.TryPrepare(owner, store, in dead, out var plan));
            foreach (var frame in sourceFrames[..^1])
            {
                Assert.True(store.TryGet(dead.Handle, out _));
                Assert.Equal(1, sourceActive[sink.Events.Count]);
                sink.Events.Add(frame[2]);
                if (frame[2] == 21 && BitConverter.ToInt16(frame, 25) is 58 or 184) sawHealing = true;
            }
            Assert.True(plan!.TryAdoptAfterLoot(out var accepted, out var publication));
            Assert.False(store.TryGet(dead.Handle, out _));
            Assert.Equal(row.GetProperty("after").GetProperty("size").GetInt32(), accepted.State.Size);
            var progress = new TerrariaInvasionProgressState(accepted.State.Progress, accepted.State.ProgressMax,
                (sbyte)accepted.State.ProgressIcon, (sbyte)accepted.State.ProgressWave);
            Assert.True(TerrariaInvasionProgressCodec.TryEncode(in progress, out var frame78));
            Assert.Equal(sourceFrames[^1], frame78);
            Assert.Equal(0, sourceActive[^1]);
            sink.Events.Add(78);
            Assert.True(publication!.TryPublish());
            Assert.Equal(23, sink.Events[^1]);
            Assert.Equal(78, sink.Events[^2]);
            Assert.False(publication.TryPublish());
            rows++;
        }
        Assert.Equal(24, rows);
        Assert.True(sawHealing);
    }

    [Fact]
    public void Loot_reentry_into_either_owner_rejects_credit_and_terminal_removal()
    {
        foreach (bool reviseNpc in new[] { false, true })
        {
            var store = new RuntimeNpcStore(2);
            var update = new NpcStateUpdate(27, 27, 800, 440, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 0, LifeMax = 100 });
            Assert.True(store.TrySpawn(0, in update, out var dead));
            var owner = new RuntimeWorldInvasion1458(State(1));
            Assert.True(RuntimeNpcInvasionDeathCredit1458.TryPrepare(owner, store, in dead, out var plan));
            if (reviseNpc) Assert.True(store.TryUpdate(dead.Handle, in update, out _));
            else
            {
                Assert.True(owner.TryCapture(out var before));
                var change = new InvasionTransition1458(before.State with { Size = 11 }, default);
                Assert.True(owner.TryAdopt(in before, in change, out _));
            }
            Assert.False(plan!.TryAdoptAfterLoot(out _, out _));
            Assert.True(store.TryGet(dead.Handle, out _));
            Assert.True(owner.TryCapture(out var current));
            Assert.Equal(reviseNpc ? 12 : 11, current.State.Size);
        }
    }

    [Fact]
    public void Generation_replacement_during_progress_publication_skips_stale_terminal_packet()
    {
        var sink = new Sink(); var store = new RuntimeNpcStore(2, sink);
        var update = new NpcStateUpdate(27, 27, 800, 440, 0, 0, 0, default,
            NpcSimulationState.Initial with { Life = 0, LifeMax = 100 });
        Assert.True(store.TrySpawn(0, in update, out var dead));
        var owner = new RuntimeWorldInvasion1458(State(1));
        Assert.True(RuntimeNpcInvasionDeathCredit1458.TryPrepare(owner, store, in dead, out var plan));
        Assert.True(plan!.TryAdoptAfterLoot(out _, out var publication));
        Assert.True(store.TrySpawn(0, in update, out var replacement));
        sink.Events.Clear();
        Assert.True(publication!.TryPublish());
        Assert.Empty(sink.Events);
        Assert.True(store.TryGet(replacement.Handle, out _));
    }

    [Fact]
    public void Extra_encounter_producers_are_not_admitted_by_a_points_table()
    {
        foreach (int type in new[] { 216, 395 })
        {
            var store = new RuntimeNpcStore(2);
            var update = new NpcStateUpdate(type, checked((short)type), 800, 440, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 0, LifeMax = 100 });
            Assert.True(store.TrySpawn(0, in update, out var dead));
            var owner = new RuntimeWorldInvasion1458(State(type == 216 ? 3 : 4));
            Assert.False(RuntimeNpcInvasionDeathCredit1458.TryPrepare(owner, store, in dead, out _));
            Assert.True(store.TryGet(dead.Handle, out _));
        }
    }

    [Fact]
    public void Saturated_event_owner_refuses_before_removing_the_dead_npc()
    {
        var store = new RuntimeNpcStore(2);
        var update = new NpcStateUpdate(27, 27, 800, 440, 0, 0, 0, default,
            NpcSimulationState.Initial with { Life = 0, LifeMax = 100 });
        Assert.True(store.TrySpawn(0, in update, out var dead));
        var owner = new RuntimeWorldInvasion1458(State(1));
        typeof(RuntimeWorldInvasion1458).GetField("revision",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(owner, long.MaxValue);
        Assert.True(RuntimeNpcInvasionDeathCredit1458.TryPrepare(owner, store, in dead, out var plan));
        Assert.False(plan!.TryAdoptAfterLoot(out _, out _));
        Assert.True(store.TryGet(dead.Handle, out _));
        Assert.True(owner.TryCapture(out var after));
        Assert.Equal(12, after.State.Size);
        Assert.Equal(long.MaxValue, after.Revision);
    }

    private class Sink : INpcStateCommitSink
    {
        internal readonly List<int> Events = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Events.Add(23);
    }
}
