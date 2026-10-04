using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcPairCommitTests
{
    [Fact]
    public void Pair_commits_both_revisions_without_publishing_either_intermediate_actor()
    {
        var sink = new Sink();
        var store = new RuntimeNpcStore(4, commitSink: sink);
        var first = State(0);
        var second = State(0);
        Assert.True(store.TrySpawn(0, in first, out var a));
        Assert.True(store.TrySpawn(1, in second, out var b));
        sink.Commits.Clear();
        first = first with { Ai = new(3, 840, 1, 0) };
        second = second with { Ai = new(4, 840, 0, 0) };
        Assert.True(store.TryUpdatePairUnpublished(in a, in first, in b, in second, out var nextA, out var nextB));
        Assert.Empty(sink.Commits);
        Assert.Equal(a.Revision.Value + 1, nextA.Revision.Value);
        Assert.Equal(b.Revision.Value + 1, nextB.Revision.Value);
        Assert.True(store.TryGet(nextA.Handle, out var liveA));
        Assert.True(store.TryGet(nextB.Handle, out var liveB));
        Assert.Equal(nextA, liveA);
        Assert.Equal(nextB, liveB);
        Assert.True(store.TryPublishUpdate(in nextA, forceSync: true));
        Assert.Single(sink.Commits);
        Assert.Equal(nextA, sink.Commits[0]);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("staleRevision")]
    [InlineData("reusedGeneration")]
    [InlineData("sameActor")]
    public void Rejected_peer_does_not_mutate_or_publish_first_actor(string fault)
    {
        var sink = new Sink();
        var store = new RuntimeNpcStore(4, commitSink: sink);
        var first = State(0);
        var second = State(0);
        Assert.True(store.TrySpawn(0, in first, out var a));
        Assert.True(store.TrySpawn(1, in second, out var b));
        if (fault == "invalid") second = second with { PositionX = float.NaN };
        if (fault == "staleRevision") Assert.True(store.TryUpdate(b.Handle, in second, out _));
        if (fault == "reusedGeneration")
        {
            Assert.True(store.TryDespawn(b.Handle));
            Assert.True(store.TrySpawn(b.Handle.Slot, in second, out _));
        }
        if (fault == "sameActor") b = a;
        Assert.True(store.TryGetActive(1, out var retainedPeer));
        sink.Commits.Clear();
        first = first with { Ai = new(3, 840, 1, 0) };
        Assert.False(store.TryUpdatePairUnpublished(in a, in first, in b, in second, out _, out _));
        Assert.True(store.TryGet(a.Handle, out var retainedA));
        Assert.True(store.TryGetActive(1, out var retainedB));
        Assert.Equal(a, retainedA);
        Assert.Equal(retainedPeer, retainedB);
        Assert.Empty(sink.Commits);
    }

    private static NpcStateUpdate State(float ai0) => new(17, 17, 639, 440, 0, 0, 255,
        new(ai0, 300, 0, 0), NpcSimulationState.Initial with { Life = 250, LifeMax = 250 });

    private sealed class Sink : INpcStateCommitSink
    {
        public List<NpcSnapshot> Commits { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Commits.Add(npc);
    }
}
