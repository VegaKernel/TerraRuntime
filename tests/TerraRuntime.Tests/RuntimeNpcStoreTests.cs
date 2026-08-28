using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcStoreTests
{
    [Fact]
    public void Slot_reuse_advances_generation_while_updates_advance_revision()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        NpcStateUpdate first = CreateUpdate(type: 1, netId: 1, ai0: 0f);

        Assert.True(store.TrySpawn(3, in first, out NpcSnapshot created));
        Assert.Equal((ulong)1, created.Handle.Generation.Value);
        Assert.Equal((ulong)1, created.Revision.Value);

        NpcStateUpdate changed = CreateUpdate(type: 1, netId: 1, ai0: 2f);
        Assert.True(store.TryUpdate(created.Handle, in changed, out NpcSnapshot updated));
        Assert.Equal(created.Handle, updated.Handle);
        Assert.Equal((ulong)2, updated.Revision.Value);
        Assert.Equal(2f, updated.Ai.Ai0);

        Assert.True(store.TryDespawn(created.Handle));
        Assert.False(store.TryGetActive(3, out _));

        NpcStateUpdate replacement = CreateUpdate(type: 2, netId: 2, ai0: 5f);
        Assert.True(store.TrySpawn(3, in replacement, out NpcSnapshot reused));
        Assert.Equal((ulong)2, reused.Handle.Generation.Value);
        Assert.Equal((ulong)1, reused.Revision.Value);
        Assert.NotEqual(created.Handle, reused.Handle);
    }

    [Fact]
    public void Stale_handle_cannot_mutate_or_despawn_reused_slot()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate first = CreateUpdate(type: 10, netId: 10, ai0: 1f);
        Assert.True(store.TrySpawn(1, in first, out NpcSnapshot original));
        Assert.True(store.TryDespawn(original.Handle));

        NpcStateUpdate replacement = CreateUpdate(type: 11, netId: 11, ai0: 2f);
        Assert.True(store.TrySpawn(1, in replacement, out NpcSnapshot current));

        NpcStateUpdate staleMutation = CreateUpdate(type: 99, netId: 99, ai0: 99f);
        Assert.False(store.TryUpdate(original.Handle, in staleMutation, out _));
        Assert.False(store.TryDespawn(original.Handle));

        Assert.True(store.TryGet(current.Handle, out NpcSnapshot stillCurrent));
        Assert.Equal(11, stillCurrent.Type);
        Assert.Equal((short)11, stillCurrent.NetId);
        Assert.Equal(2f, stillCurrent.Ai.Ai0);
        Assert.Equal((ulong)1, stillCurrent.Revision.Value);
    }

    [Fact]
    public void Negative_net_id_keeps_positive_gameplay_type_separate()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate variant = CreateUpdate(type: 1, netId: -3, ai0: 0f);

        Assert.True(store.TrySpawn(0, in variant, out NpcSnapshot snapshot));
        Assert.Equal(1, snapshot.Type);
        Assert.Equal((short)-3, snapshot.NetId);
    }

    [Fact]
    public void Non_positive_gameplay_type_is_rejected()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate invalid = CreateUpdate(type: 0, netId: 1, ai0: 0f);

        Assert.False(store.TrySpawn(0, in invalid, out _));
        Assert.Equal(0, store.ActiveCount);
    }

    [Fact]
    public void CopyActive_is_bounded_and_returns_slots_in_stable_order()
    {
        var store = new RuntimeNpcStore(capacity: 16);
        NpcStateUpdate update = CreateUpdate(type: 50, netId: 50, ai0: 0f);
        Assert.True(store.TrySpawn(9, in update, out _));
        Assert.True(store.TrySpawn(2, in update, out _));

        Span<NpcSnapshot> snapshots = stackalloc NpcSnapshot[16];
        int count = store.CopyActive(snapshots);

        Assert.Equal(2, count);
        Assert.Equal((byte)2, snapshots[0].Handle.Slot);
        Assert.Equal((byte)9, snapshots[1].Handle.Slot);

        var tooSmall = new NpcSnapshot[1];
        Assert.Throws<ArgumentException>(() => store.CopyActive(tooSmall));
    }

    [Fact]
    public void Non_finite_motion_ai_or_simulation_state_is_rejected_without_occupying_slot()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate badPosition = CreateUpdate(type: 1, netId: 1, ai0: 0f) with { PositionX = float.NaN };
        NpcStateUpdate badAi = CreateUpdate(type: 1, netId: 1, ai0: float.PositiveInfinity);
        NpcStateUpdate badSimulation = CreateUpdate(type: 1, netId: 1, ai0: 0f) with
        {
            Simulation = NpcSimulationState.Initial with { Scale = 0f }
        };

        Assert.False(store.TrySpawn(0, in badPosition, out _));
        Assert.False(store.TrySpawn(1, in badAi, out _));
        Assert.False(store.TrySpawn(2, in badSimulation, out _));
        Assert.Equal(0, store.ActiveCount);
    }

    [Fact]
    public void Capacity_is_bounded_by_packet_23_byte_slot_addressability()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeNpcStore(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeNpcStore(capacity: RuntimeNpcStore.MaximumAddressableCapacity + 1));

        var maximum = new RuntimeNpcStore(RuntimeNpcStore.MaximumAddressableCapacity);
        NpcStateUpdate update = CreateUpdate(type: 1, netId: 1, ai0: 0f);
        Assert.True(maximum.TrySpawn(byte.MaxValue, in update, out NpcSnapshot snapshot));
        Assert.Equal(byte.MaxValue, snapshot.Handle.Slot);
    }

    [Fact]
    public void Active_slot_cannot_be_spawned_over_without_explicit_despawn()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate first = CreateUpdate(type: 1, netId: 1, ai0: 0f);
        NpcStateUpdate replacement = CreateUpdate(type: 2, netId: 2, ai0: 0f);

        Assert.True(store.TrySpawn(0, in first, out NpcSnapshot created));
        Assert.False(store.TrySpawn(0, in replacement, out _));
        Assert.True(store.TryGet(created.Handle, out NpcSnapshot current));
        Assert.Equal(1, current.Type);
        Assert.Equal((short)1, current.NetId);
        Assert.Equal((ulong)1, current.Revision.Value);
    }

    private static NpcStateUpdate CreateUpdate(int type, short netId, float ai0) =>
        new(
            Type: type,
            NetId: netId,
            PositionX: 120f,
            PositionY: 240f,
            VelocityX: 1.5f,
            VelocityY: -2f,
            Target: ushort.MaxValue,
            Ai: new NpcAiState(ai0, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial);
}
