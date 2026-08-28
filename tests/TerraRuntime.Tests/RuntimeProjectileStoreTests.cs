using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileStoreTests
{
    [Fact]
    public void Slot_reuse_advances_generation_while_updates_advance_revision()
    {
        var store = new RuntimeProjectileStore(capacity: 16);
        ProjectileStateUpdate first = CreateUpdate(type: 1, positionX: 10f);

        Assert.True(store.TrySpawn(7, in first, out ProjectileSnapshot created));
        Assert.Equal((ulong)1, created.Handle.Generation.Value);
        Assert.Equal((ulong)1, created.Revision.Value);
        Assert.Equal(new ProjectileTypeId(1), created.Type);

        ProjectileStateUpdate changed = CreateUpdate(type: 1, positionX: 20f);
        Assert.True(store.TryUpdate(created.Handle, in changed, out ProjectileSnapshot updated));
        Assert.Equal(created.Handle, updated.Handle);
        Assert.Equal((ulong)2, updated.Revision.Value);
        Assert.Equal(20f, updated.PositionX);

        Assert.True(store.TryDespawn(created.Handle, out ProjectileSnapshot finalSnapshot));
        Assert.Equal(updated.Handle, finalSnapshot.Handle);
        Assert.Equal(updated.Revision, finalSnapshot.Revision);
        Assert.False(store.TryGetActive(7, out _));

        ProjectileStateUpdate replacement = CreateUpdate(type: 2, positionX: 30f);
        Assert.True(store.TrySpawn(7, in replacement, out ProjectileSnapshot reused));
        Assert.Equal((ulong)2, reused.Handle.Generation.Value);
        Assert.Equal((ulong)1, reused.Revision.Value);
        Assert.NotEqual(created.Handle, reused.Handle);
    }

    [Fact]
    public void Spawn_initializes_and_type_change_resets_non_wire_lifecycle_defaults()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate arrow = CreateUpdate(type: 1, positionX: 10f);
        Assert.True(store.TrySpawn(0, in arrow, out ProjectileSnapshot spawned));
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState arrowLifecycle));
        Assert.Equal(new ProjectileLifecycleState(1200, false), arrowLifecycle);

        ProjectileStateUpdate sameType = arrow with { PositionX = 20f };
        Assert.True(store.TryUpdate(spawned.Handle, in sameType, out ProjectileSnapshot moved));
        Assert.True(store.TryGetLifecycle(moved.Handle, out ProjectileLifecycleState preserved));
        Assert.Equal(arrowLifecycle, preserved);

        ProjectileStateUpdate importantType = sameType with { Type = new ProjectileTypeId(13) };
        Assert.True(store.TryUpdate(moved.Handle, in importantType, out ProjectileSnapshot changedType));
        Assert.True(store.TryGetLifecycle(changedType.Handle, out ProjectileLifecycleState reset));
        Assert.Equal(new ProjectileLifecycleState(36000, true), reset);
    }

    [Fact]
    public void Vanilla_full_pool_replaces_lowest_timeLeft_non_netImportant_without_despawn_commit()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeProjectileStore(commitSink: sink);
        ProjectileHandle oldestHandle = default;

        for (ushort slot = 0; slot < RuntimeProjectileStore.VanillaPhysicalSlotCount; slot++)
        {
            int type = slot == 123 ? 1122 : 3;
            ProjectileStateUpdate state = CreateUpdate(type, positionX: slot);
            Assert.True(store.TrySpawn(slot, in state, out ProjectileSnapshot spawned));
            if (slot == 123)
                oldestHandle = spawned.Handle;
        }

        Assert.Equal(RuntimeProjectileStore.VanillaPhysicalSlotCount, store.ActiveCount);
        sink.Commits.Clear();

        ProjectileStateUpdate replacement = CreateUpdate(type: 1, positionX: 9000f);
        Assert.True(store.TrySpawnVanilla(in replacement, out ProjectileSnapshot reused));

        Assert.Equal((ushort)123, reused.Handle.Slot);
        Assert.Equal((ulong)2, reused.Handle.Generation.Value);
        Assert.Equal(RuntimeProjectileStore.VanillaPhysicalSlotCount, store.ActiveCount);
        Assert.False(store.TryGet(oldestHandle, out _));
        Assert.True(store.TryGetLifecycle(reused.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(new ProjectileLifecycleState(1200, false), lifecycle);
        Assert.Single(sink.Commits);
        Assert.Equal(ProjectileStateCommitKind.Spawn, sink.Commits[0].Kind);
        Assert.Equal(reused, sink.Commits[0].Snapshot);
    }

    [Fact]
    public void Vanilla_full_netImportant_pool_uses_and_reuses_overflow_slot_1000()
    {
        var store = new RuntimeProjectileStore();
        for (ushort slot = 0; slot < RuntimeProjectileStore.VanillaPhysicalSlotCount; slot++)
        {
            ProjectileStateUpdate important = CreateUpdate(type: 13, positionX: slot);
            Assert.True(store.TrySpawn(slot, in important, out _));
        }

        ProjectileStateUpdate firstOverflow = CreateUpdate(type: 1, positionX: 5000f);
        Assert.True(store.TrySpawnVanilla(in firstOverflow, out ProjectileSnapshot first));
        Assert.Equal(RuntimeProjectileStore.VanillaOverflowSlot, first.Handle.Slot);
        Assert.Equal((ulong)1, first.Handle.Generation.Value);
        Assert.Equal(RuntimeProjectileStore.MaximumProtocolAddressableCapacity, store.ActiveCount);

        ProjectileStateUpdate secondOverflow = CreateUpdate(type: 2, positionX: 6000f);
        Assert.True(store.TrySpawnVanilla(in secondOverflow, out ProjectileSnapshot second));
        Assert.Equal(RuntimeProjectileStore.VanillaOverflowSlot, second.Handle.Slot);
        Assert.Equal((ulong)2, second.Handle.Generation.Value);
        Assert.False(store.TryGet(first.Handle, out _));
        Assert.Equal(RuntimeProjectileStore.MaximumProtocolAddressableCapacity, store.ActiveCount);
    }

    [Fact]
    public void Reduced_capacity_store_does_not_fake_a_full_1000_slot_vanilla_pool()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateUpdate(type: 3, positionX: 1f);
        for (ushort slot = 0; slot < store.Capacity; slot++)
            Assert.True(store.TrySpawn(slot, in state, out _));

        ProjectileStateUpdate extra = CreateUpdate(type: 1, positionX: 2f);
        Assert.False(store.TrySpawnVanilla(in extra, out _));
        Assert.Equal(4, store.ActiveCount);
    }

    [Fact]
    public void Successful_mutations_publish_committed_snapshots_only()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeProjectileStore(capacity: 4, commitSink: sink);
        ProjectileStateUpdate state = CreateUpdate(type: 1, positionX: 10f);

        Assert.True(store.TrySpawn(2, in state, out ProjectileSnapshot spawned));
        ProjectileStateUpdate moved = state with { PositionX = 20f };
        Assert.True(store.TryUpdate(spawned.Handle, in moved, out ProjectileSnapshot updated));
        Assert.False(store.TryUpdate(
            new ProjectileHandle(2, new ProjectileGeneration(99)),
            in moved,
            out _));
        Assert.True(store.TryDespawn(updated.Handle, out ProjectileSnapshot despawned));
        Assert.False(store.TryDespawn(updated.Handle, out _));

        Assert.Equal(3, sink.Commits.Count);
        Assert.Equal(ProjectileStateCommitKind.Spawn, sink.Commits[0].Kind);
        Assert.Equal(spawned, sink.Commits[0].Snapshot);
        Assert.Equal(ProjectileStateCommitKind.Update, sink.Commits[1].Kind);
        Assert.Equal(updated, sink.Commits[1].Snapshot);
        Assert.Equal(ProjectileStateCommitKind.Despawn, sink.Commits[2].Kind);
        Assert.Equal(despawned, sink.Commits[2].Snapshot);
    }

    [Fact]
    public void Stale_handle_cannot_mutate_or_despawn_reused_slot()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateUpdate(type: 1, positionX: 1f);
        Assert.True(store.TrySpawn(2, in state, out ProjectileSnapshot first));
        Assert.True(store.TryDespawn(first.Handle, out _));
        Assert.True(store.TrySpawn(2, in state, out ProjectileSnapshot replacement));

        ProjectileStateUpdate staleUpdate = CreateUpdate(type: 2, positionX: 99f);
        Assert.False(store.TryUpdate(first.Handle, in staleUpdate, out _));
        Assert.False(store.TryDespawn(first.Handle, out _));
        Assert.True(store.TryGet(replacement.Handle, out ProjectileSnapshot current));
        Assert.Equal(1f, current.PositionX);
    }

    [Fact]
    public void CopyActive_is_bounded_and_stable_by_slot()
    {
        var store = new RuntimeProjectileStore(capacity: 8);
        ProjectileStateUpdate state = CreateUpdate(type: 1, positionX: 1f);
        Assert.True(store.TrySpawn(6, in state, out _));
        Assert.True(store.TrySpawn(1, in state, out _));

        Span<ProjectileSnapshot> snapshots = stackalloc ProjectileSnapshot[8];
        int count = store.CopyActive(snapshots);

        Assert.Equal(2, count);
        Assert.Equal((ushort)1, snapshots[0].Handle.Slot);
        Assert.Equal((ushort)6, snapshots[1].Handle.Slot);
        Assert.Throws<ArgumentException>(() => store.CopyActive(new ProjectileSnapshot[1]));
    }

    [Fact]
    public void Protocol_addressability_ceiling_includes_the_real_vanilla_overflow_slot()
    {
        var store = new RuntimeProjectileStore();
        ProjectileStateUpdate state = CreateUpdate(type: 1, positionX: 1f);

        Assert.Equal((ushort)1000, RuntimeProjectileStore.VanillaOverflowSlot);
        Assert.Equal(RuntimeProjectileStore.VanillaOverflowSlot, RuntimeProjectileStore.MaximumProtocolIndex);
        Assert.Equal(1001, RuntimeProjectileStore.MaximumProtocolAddressableCapacity);
        Assert.True(store.TrySpawn(RuntimeProjectileStore.VanillaOverflowSlot, in state, out _));
        Assert.False(store.TrySpawn(1001, in state, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeProjectileStore(RuntimeProjectileStore.MaximumProtocolAddressableCapacity + 1));
    }

    [Fact]
    public void Non_finite_undefined_or_protocol_unrepresentable_state_is_rejected()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate nonFinite = CreateUpdate(type: 1, positionX: float.NaN);
        ProjectileStateUpdate undefinedType = CreateUpdate(type: 457, positionX: 1f);
        ProjectileStateUpdate oversizedType = CreateUpdate(type: short.MaxValue + 1, positionX: 1f);
        ProjectileStateUpdate invalidAi = CreateUpdate(type: 1, positionX: 1f) with
        {
            Ai = new ProjectileAiState(float.PositiveInfinity, 0f, 0f)
        };

        Assert.False(store.TrySpawn(0, in nonFinite, out _));
        Assert.False(store.TrySpawn(0, in undefinedType, out _));
        Assert.False(store.TrySpawn(0, in oversizedType, out _));
        Assert.False(store.TrySpawn(0, in invalidAi, out _));
        Assert.Equal(0, store.ActiveCount);
    }

    private static ProjectileStateUpdate CreateUpdate(int type, float positionX) =>
        new(
            Type: new ProjectileTypeId(type),
            Spawner: 3,
            PositionX: positionX,
            PositionY: 200f,
            VelocityX: 4f,
            VelocityY: -5f,
            Ai: new ProjectileAiState(1f, 2f, 3f),
            BannerIdToRespondTo: 0,
            Damage: 25,
            KnockBack: 2.5f,
            OriginalDamage: 25);

    private sealed class RecordingCommitSink : IProjectileStateCommitSink
    {
        public List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> Commits { get; } = [];

        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
            Commits.Add((kind, snapshot));
    }
}
