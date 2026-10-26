using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class NpcCatchOwnership1458Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Item_publication_cannot_update_or_catch_the_retired_actor_or_remove_its_replacement(bool reuseSlot)
    {
        var sink = new Publications();
        var npcs = new RuntimeNpcStore(capacity: 1, commitSink: sink);
        var items = new RuntimeWorldItemStore(sink);
        var random = new SystemWorldItemSpawnRandom(1458);
        var state = new ServerRuntimeState(npcs: npcs, worldItems: items, worldItemSpawnRandom: random);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9070), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 100, 0, 0, 0, 0, 0)));
        state.Apply(new PlayerMovementRuntimeCommand(connection,
            new(session.Slot, 0, 0, 0, 0, 0, 1000.25f, 1000.75f, false, 0, 0, false, 0,
                false, 0, 0, 0, 0, false, 0, 0)));
        var creation = new NpcStateUpdate(46, 46, 1000, 1000, 0, 0, 255, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawnVanilla(in creation, out var birth));
        var command = new ClientNpcCatchRuntimeCommand(connection, new(birth.Handle.Slot));
        NpcSnapshot replacement = default;
        sink.OnDrop = () =>
        {
            Assert.False(npcs.TryGet(birth.Handle, out _));
            Assert.False(npcs.TryUpdate(birth.Handle, in creation, out _));
            state.Apply(command);
            Assert.Equal(1, items.ActiveCount);
            if (reuseSlot)
            {
                Assert.False(npcs.TrySpawnVanilla(in creation, out _));
                state.Tick();
                state.Tick();
                Assert.True(npcs.TrySpawnVanilla(in creation, out replacement));
                Assert.NotEqual(birth.Handle.Generation, replacement.Handle.Generation);
            }
        };
        state.Apply(command);
        Assert.Equal(1, sink.Drops);
        Assert.Equal(1, items.ActiveCount);
        Assert.False(npcs.TryGet(birth.Handle, out _));
        Assert.Equal(1916656655, random.SourceRandom.Next());
        if (reuseSlot)
        {
            Assert.True(npcs.TryGet(replacement.Handle, out var current));
            Assert.Equal(replacement, current);
            Assert.Empty(sink.Retired);
        }
        else
        {
            Assert.Equal(0, npcs.ActiveCount);
            Assert.Equal(birth, Assert.Single(sink.Retired));
        }
    }

    private sealed class Publications : IWorldItemStateCommitSink, INpcStateCommitSink
    {
        internal Action? OnDrop;
        internal int Drops;
        internal List<NpcSnapshot> Retired { get; } = [];
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            if (kind != WorldItemStateCommitKind.Drop) return;
            Drops++;
            OnDrop?.Invoke();
        }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        {
            if (kind == NpcStateCommitKind.Despawn) Retired.Add(snapshot);
        }
    }
}
