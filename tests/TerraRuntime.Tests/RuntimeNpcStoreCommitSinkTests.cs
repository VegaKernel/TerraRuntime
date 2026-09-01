using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcStoreCommitSinkTests
{
    [Fact]
    public void Store_publishes_spawn_update_and_final_despawn_snapshot_after_successful_commits()
    {
        var sink = new RecordingSink();
        var store = new RuntimeNpcStore(capacity: 2, commitSink: sink);
        NpcStateUpdate initial = CreateUpdate(100f);

        Assert.True(store.TrySpawn(1, in initial, out NpcSnapshot spawned));
        NpcStateUpdate moved = initial with { PositionX = 125f };
        Assert.True(store.TryUpdate(spawned.Handle, in moved, out NpcSnapshot updated));
        Assert.True(store.TryDespawn(spawned.Handle));

        Assert.Equal(3, sink.Events.Count);
        Assert.Equal(NpcStateCommitKind.Spawn, sink.Events[0].Kind);
        Assert.Equal(new NpcRevision(1), sink.Events[0].Snapshot.Revision);
        Assert.Equal(NpcStateCommitKind.Update, sink.Events[1].Kind);
        Assert.Equal(new NpcRevision(2), sink.Events[1].Snapshot.Revision);
        Assert.Equal(125f, sink.Events[1].Snapshot.PositionX);
        Assert.Equal(NpcStateCommitKind.Despawn, sink.Events[2].Kind);
        Assert.Equal(spawned.Handle, sink.Events[2].Snapshot.Handle);
        Assert.Equal(new NpcRevision(2), sink.Events[2].Snapshot.Revision);
        Assert.Equal(125f, sink.Events[2].Snapshot.PositionX);
        Assert.False(store.TryGet(spawned.Handle, out _));
    }

    [Fact]
    public void Rejected_stale_mutations_publish_nothing()
    {
        var sink = new RecordingSink();
        var store = new RuntimeNpcStore(capacity: 1, commitSink: sink);
        NpcStateUpdate initial = CreateUpdate(10f);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot first));
        Assert.True(store.TryDespawn(first.Handle));
        Assert.True(store.TrySpawn(0, in initial, out _));
        int before = sink.Events.Count;

        Assert.False(store.TryUpdate(first.Handle, in initial, out _));
        Assert.False(store.TryDespawn(first.Handle));

        Assert.Equal(before, sink.Events.Count);
    }

    private static NpcStateUpdate CreateUpdate(float positionX) =>
        new(
            Type: 1,
            NetId: 1,
            PositionX: positionX,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

    private sealed class RecordingSink : INpcStateCommitSink
    {
        public List<(NpcStateCommitKind Kind, NpcSnapshot Snapshot)> Events { get; } = [];

        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) =>
            Events.Add((kind, snapshot));
    }
}
