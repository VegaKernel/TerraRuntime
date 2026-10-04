using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class SlimeBirthOwnership1458Tests
{
    [Fact]
    public void First_outer_update_announces_pending_generation_once()
    {
        var sink = new Sink();
        var store = new RuntimeNpcStore(4, sink);
        var state = State(210);
        Assert.True(store.TrySpawn(1, in state, out var birth));
        sink.Events.Clear();
        Assert.True(store.TryRetainPendingBirth(in birth));
        Assert.True(store.TryUpdate(birth.Handle, in state, out var first));
        Assert.True(store.TryUpdate(first.Handle, in state, out _));
        Assert.Equal(new[] { NpcStateCommitKind.Spawn, NpcStateCommitKind.Update }, sink.Events.Select(e => e.Kind));
        Assert.Equal(2UL, sink.Events[0].Npc.Revision.Value);
    }

    [Fact]
    public void Stale_birth_handle_cannot_flush_reused_generation()
    {
        var sink = new Sink();
        var store = new RuntimeNpcStore(2, sink);
        var state = State(210);
        Assert.True(store.TrySpawn(0, in state, out var old));
        Assert.True(store.TryDespawn(old.Handle));
        Assert.True(store.TrySpawn(0, in state, out var current));
        Assert.True(store.TryRetainPendingBirth(in current));
        sink.Events.Clear();
        Assert.False(store.TryPublishPendingBirth(old.Handle));
        Assert.Empty(sink.Events);
        Assert.True(store.TryPublishPendingBirth(current.Handle));
        Assert.True(store.TryPublishPendingBirth(current.Handle));
        Assert.Single(sink.Events);
        Assert.Equal(current.Handle, sink.Events[0].Npc.Handle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Adoption_commits_child_and_shared_stream_before_first_publication(bool good)
    {
        var sink = new Sink();
        var random = new VanillaUnifiedRandom1458(18);
        var store = new RuntimeNpcStore(4, sink);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        store.SetVanillaSpawnContextSource(() => new(1f, 1, good));
        var parentState = State(1);
        Assert.True(store.TrySpawn(2, in parentState, out var parent));
        sink.Events.Clear();
        Assert.True(store.TryCreateAiSpawnPreview(in parent, random, out var preview));
        var intent = new NpcAiSpawnIntent(VanillaNpcIds.Bee, 1000, 1000, 0f, 0f, 255);
        Assert.True(preview!.TryStageHiveChild(in intent, out var birth));
        Assert.True(preview.TryFinishHiveChild(in birth, 1f, -1f, wet: true));
        int expectedNext = preview.Random.Clone().Next();
        var placeholder = State(in parent);
        Assert.True(store.TryUpdateUnpublished(parent.Handle, in placeholder, out var accepted));
        var planned = placeholder with { Ai = new(50f, 1124f, 1f, 0f) };
        Assert.True(preview.TryAdopt(in accepted, in planned, out var completed));
        Assert.Equal(planned.Ai, completed.Ai);
        Assert.Empty(sink.Events);
        Assert.Equal(expectedNext, random.Next());
        Assert.True(store.TryGet(birth.Handle, out var child));
        Assert.True(child.Simulation.Wet);
        Assert.Equal(60f, child.Ai.Ai1);
        Assert.Equal(60f, child.Simulation.LocalAi.Ai0);
        Assert.Equal(994f, child.PositionX);
        Assert.Equal(988f, child.PositionY);
        Assert.False(preview.TryAdopt(in completed, in parentState, out _));
        Assert.True(store.TryPublishPendingBirth(child.Handle));
        Assert.Single(sink.Events);
        Assert.Equal(NpcStateCommitKind.Spawn, sink.Events[0].Kind);
    }

    [Theory]
    [InlineData("peer")]
    [InlineData("random")]
    [InlineData("context")]
    [InlineData("protection")]
    [InlineData("pending")]
    public void Stale_preview_rejects_without_adopting_a_child_or_overwriting_new_draws(string changed)
    {
        var random = new VanillaUnifiedRandom1458(18);
        var store = new RuntimeNpcStore(4);
        var context = new VanillaNpcSpawnContext(1f, 1, false);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        store.SetVanillaSpawnContextSource(() => context);
        var parentState = State(1);
        Assert.True(store.TrySpawn(2, in parentState, out var parent));
        var peerState = State(210);
        Assert.True(store.TrySpawn(3, in peerState, out var peer));
        Assert.True(store.TryCreateAiSpawnPreview(in parent, random, out var preview));
        var intent = new NpcAiSpawnIntent(VanillaNpcIds.Bee, 1000, 1000, 0f, 0f, 255);
        Assert.True(preview!.TryStageHiveChild(in intent, out var birth));
        Assert.True(preview.TryFinishHiveChild(in birth, 1f, -1f, wet: false));
        var placeholder = State(in parent);
        Assert.True(store.TryUpdateUnpublished(parent.Handle, in placeholder, out var accepted));
        switch (changed)
        {
            case "peer": Assert.True(store.TryUpdate(peer.Handle, in peerState, out _)); break;
            case "random": random.Next(); break;
            case "context": context = context with { GoodWorld = true }; break;
            case "pending": Assert.True(store.TryRetainPendingBirth(in peer)); break;
            case "protection":
                Assert.True(store.TrySpawnVanilla(in peerState, out _));
                store.UpdateProtectedSpawnSlots();
                break;
        }
        var expectedRandom = random.Clone();
        int expectedCount = store.ActiveCount;
        Assert.False(preview.TryAdopt(in accepted, in parentState, out _));
        Assert.True(random.HasSameState(expectedRandom));
        Assert.Equal(expectedCount, store.ActiveCount);
        Assert.True(store.TryGet(parent.Handle, out var unchanged));
        Assert.Equal(accepted, unchanged);
    }

    [Fact]
    public void Foreign_placeholder_update_is_rejected_without_adopting_speculative_stream()
    {
        var random = new VanillaUnifiedRandom1458(18);
        var store = new RuntimeNpcStore(4);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        var input = State(1);
        Assert.True(store.TrySpawn(2, in input, out var before));
        Assert.True(store.TryCreateAiSpawnPreview(in before, random, out var preview));
        preview!.Random.Next();
        var foreign = State(in before) with { Ai = new(999f, 0f, 0f, 0f) };
        Assert.True(store.TryUpdateUnpublished(before.Handle, in foreign, out var accepted));
        var checkpoint = random.Clone();
        Assert.False(preview.TryAdopt(in accepted, in input, out _));
        Assert.True(random.HasSameState(checkpoint));
        Assert.True(store.TryGet(before.Handle, out var current));
        Assert.Equal(accepted, current);
    }

    [Fact]
    public void Context_callback_cannot_rebase_a_same_value_peer_write()
    {
        var random = new VanillaUnifiedRandom1458(18);
        var store = new RuntimeNpcStore(4);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        var input = State(1);
        Assert.True(store.TrySpawn(2, in input, out var before));
        Assert.True(store.TrySpawn(3, in input, out var peer));
        var unchanged = State(in peer);
        store.SetVanillaSpawnContextSource(() =>
        {
            Assert.True(store.TryUpdateUnpublished(peer.Handle, in unchanged, out _));
            return new(1f, 1, false);
        });
        var checkpoint = random.Clone();
        Assert.False(store.TryCreateAiSpawnPreview(in before, random, out _));
        Assert.True(random.HasSameState(checkpoint));
        Assert.True(store.TryGet(before.Handle, out var current));
        Assert.Equal(before, current);
    }

    [Fact]
    public void Completed_spawn_pass_flushes_pending_physical_slots_in_ascending_order_once()
    {
        var sink = new Sink();
        var store = new RuntimeNpcStore(4, sink);
        var input = State(210);
        Assert.True(store.TrySpawn(2, in input, out var higher));
        Assert.True(store.TrySpawn(0, in input, out var lower));
        Assert.True(store.TryRetainPendingBirth(in higher));
        Assert.True(store.TryRetainPendingBirth(in lower));
        sink.Events.Clear();
        store.PublishPendingBirths();
        store.PublishPendingBirths();
        Assert.Equal(new byte[] { 0, 2 }, sink.Events.Select(e => e.Npc.Handle.Slot));
        Assert.All(sink.Events, e => Assert.Equal(NpcStateCommitKind.Spawn, e.Kind));
    }

    private static NpcStateUpdate State(int type) => new(type, checked((short)type), 100f, 100f,
        0f, 0f, 255, default, NpcSimulationState.Initial);

    private static NpcStateUpdate State(in NpcSnapshot npc) => new(npc.Type, npc.NetId,
        npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private sealed class Sink : INpcStateCommitSink
    {
        internal readonly List<(NpcStateCommitKind Kind, NpcSnapshot Npc)> Events = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Events.Add((kind, snapshot));
    }
}
