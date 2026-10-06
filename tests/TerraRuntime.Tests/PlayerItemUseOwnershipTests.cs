using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Tests;

public sealed class PlayerItemUseOwnershipTests
{
    [Fact]
    public void Inventory_checkpoint_rejects_same_value_write_and_connection_reuse()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(1);
        var item = new PlayerEquipmentCommitRequest(connection.Player.Slot, 0, 7, 0, 1, 0);
        Assert.True(store.TrySet(connection, in item));
        ulong before = store.Serial;
        RuntimePlayerInventoryMutation[] change = [new(0, default)];
        Assert.True(store.TrySet(connection, in item));
        Assert.False(store.TryApplyAtomic(connection, change, before));
        Assert.True(store.TryGet(connection, 0, out RuntimePlayerInventoryItem retained));
        Assert.Equal((short)7, retained.Stack);

        before = store.Serial;
        store.Clear(connection);
        Assert.True(store.TryAttach(Connection(2)));
        Assert.False(store.TryIsCurrent(connection, before));
        Assert.False(store.TryApplyAtomic(connection, change, before));
    }

    [Fact]
    public void Inventory_checkpoint_adopts_only_whole_valid_batch_and_rejects_saturation()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(1);
        Assert.True(store.TryAttach(connection));
        ulong before = store.Serial;
        RuntimePlayerInventoryMutation[] invalid = [new(0, default), new(0, default)];
        Assert.False(store.TryApplyAtomic(connection, invalid, before));
        Assert.Equal(before, store.Serial);
        RuntimePlayerInventoryMutation[] valid =
            [new(0, new RuntimePlayerInventoryItem(new ItemTypeId(1), 2, default, 0))];
        Assert.True(store.TryApplyAtomic(connection, valid, before));
        Assert.Equal(before + 1, store.Serial);
        Assert.False(store.TryApplyAtomic(connection, valid, before));

        typeof(RuntimePlayerInventoryStore).GetProperty("Serial",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(store, ulong.MaxValue);
        Assert.False(store.TryIsCurrent(connection, ulong.MaxValue));
        Assert.False(store.TryApplyAtomic(connection, valid, ulong.MaxValue));
    }

    [Fact]
    public void Prepared_spawn_has_no_mutation_until_commit_and_publishes_once_after_trust()
    {
        var sink = new Sink();
        var store = new RuntimeProjectileStore(2, sink);
        ProjectileStateUpdate update = Update(1);
        Assert.True(store.TryPrepareVanillaSpawn(in update, 40, out var plan));
        Assert.NotNull(plan);
        Assert.Equal(0, store.ActiveCount);
        Assert.Empty(sink.Commits);
        Assert.True(plan.IsCurrent);
        Assert.True(plan.TryCommitUnpublished(out ProjectileSnapshot created));
        Assert.Empty(sink.Commits);
        Assert.Equal(1UL, created.Handle.Generation.Value);
        Assert.True(store.TryGetLifecycle(created.Handle, out var lifecycle));
        Assert.Equal(40, lifecycle.TimeLeft);
        Assert.True(store.TryMarkCombatTrusted(created.Handle));
        bool entered = false;
        sink.OnCommit = snapshot =>
        {
            Assert.True(store.IsCombatTrusted(snapshot.Handle));
            if (entered)
                return;
            entered = true;
            Assert.False(plan.TryPublish(in snapshot));
        };
        Assert.True(plan.TryPublish(in created));
        Assert.Single(sink.Commits);
        Assert.False(plan.TryPublish(in created));
        Assert.False(plan.TryCommitUnpublished(out _));
    }

    [Fact]
    public void Prepared_spawn_rejects_slot_ABA_and_same_value_revision_change()
    {
        var store = new RuntimeProjectileStore(2);
        ProjectileStateUpdate update = Update(1);
        Assert.True(store.TryPrepareVanillaSpawn(in update, null, out var plan));
        Assert.True(store.TrySpawn(0, in update, out var other));
        Assert.True(store.TryDespawn(other.Handle, out _));
        Assert.False(plan!.IsCurrent);
        Assert.False(plan.TryCommitUnpublished(out _));
        Assert.True(store.TrySpawn(0, in update, out var final));
        Assert.Equal(2UL, final.Handle.Generation.Value);
    }

    [Fact]
    public void Small_full_pool_rejection_preserves_existing_generation_and_frames()
    {
        var sink = new Sink();
        var store = new RuntimeProjectileStore(1, sink);
        ProjectileStateUpdate update = Update(1);
        Assert.True(store.TrySpawn(0, in update, out var existing));
        sink.Commits.Clear();
        Assert.False(store.TryPrepareVanillaSpawn(in update, null, out _));
        Assert.True(store.TryGet(existing.Handle, out var retained));
        Assert.Equal(existing, retained);
        Assert.Empty(sink.Commits);
    }

    [Fact]
    public void Prepared_spawn_rejects_exhausted_generation_without_state_change()
    {
        var store = new RuntimeProjectileStore(1);
        var slots = (Array)typeof(RuntimeProjectileStore).GetField("_slots",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(store)!;
        object state = slots.GetValue(0)!;
        state.GetType().GetField("Generation")!.SetValue(state, ulong.MaxValue);
        slots.SetValue(state, 0);
        ProjectileStateUpdate update = Update(1);
        Assert.False(store.TryPrepareVanillaSpawn(in update, null, out _));
        Assert.Equal(0, store.ActiveCount);
        Assert.Equal(ulong.MaxValue, slots.GetValue(0)!.GetType().GetField("Generation")!
            .GetValue(slots.GetValue(0)));
    }

    [Fact]
    public void Prepared_spawn_rejects_new_earlier_free_slot_even_when_selected_baseline_is_unchanged()
    {
        var store = new RuntimeProjectileStore(2);
        ProjectileStateUpdate update = Update(1);
        Assert.True(store.TrySpawn(0, in update, out var first));
        Assert.True(store.TryPrepareVanillaSpawn(in update, null, out var plan));
        Assert.True(store.TryDespawn(first.Handle, out _));
        Assert.False(plan!.IsCurrent);
        Assert.False(plan.TryCommitUnpublished(out _));
        Assert.False(store.TryGetActive(1, out _));
    }

    [Fact]
    public void Prepared_full_pool_replacement_keeps_source_selection_and_detects_changed_candidate()
    {
        var sink = new Sink();
        var store = new RuntimeProjectileStore(commitSink: sink);
        ProjectileHandle oldest = default;
        for (ushort slot = 0; slot < RuntimeProjectileStore.VanillaPhysicalSlotCount; slot++)
        {
            ProjectileStateUpdate update = Update(slot == 123 ? 1122 : 3);
            Assert.True(store.TrySpawn(slot, in update, out var spawned));
            if (slot == 123)
                oldest = spawned.Handle;
        }
        sink.Commits.Clear();
        ProjectileStateUpdate replacement = Update(1);
        Assert.True(store.TryPrepareVanillaSpawn(in replacement, null, out var rejected));
        Assert.True(store.TryGet(oldest, out var previous));
        ProjectileStateUpdate same = Update(1122);
        Assert.True(store.TryUpdate(oldest, in same, out var revised));
        Assert.False(rejected!.TryCommitUnpublished(out _));
        Assert.Equal(previous.Handle, revised.Handle);

        sink.Commits.Clear();
        Assert.True(store.TryPrepareVanillaSpawn(in replacement, null, out var plan));
        Assert.True(plan!.TryCommitUnpublished(out var committed));
        Assert.Equal((ushort)123, committed.Handle.Slot);
        Assert.Equal(oldest.Generation.Value + 1, committed.Handle.Generation.Value);
        Assert.Equal(RuntimeProjectileStore.VanillaPhysicalSlotCount, store.ActiveCount);
        Assert.Empty(sink.Commits);
        Assert.True(plan.TryPublish(in committed));
        Assert.Equal(ProjectileStateCommitKind.Spawn, Assert.Single(sink.Commits));
    }

    [Fact]
    public void Prepared_important_pool_uses_source_overflow_and_stale_publication_is_rejected()
    {
        var sink = new Sink();
        var store = new RuntimeProjectileStore(commitSink: sink);
        ProjectileStateUpdate important = Update(13);
        for (ushort slot = 0; slot < RuntimeProjectileStore.VanillaPhysicalSlotCount; slot++)
            Assert.True(store.TrySpawn(slot, in important, out _));
        sink.Commits.Clear();
        Assert.True(store.TryPrepareVanillaSpawn(in important, null, out var plan));
        Assert.True(plan!.TryCommitUnpublished(out var committed));
        Assert.Equal(RuntimeProjectileStore.VanillaOverflowSlot, committed.Handle.Slot);
        Assert.True(store.TryUpdate(committed.Handle, in important, out _));
        sink.Commits.Clear();
        Assert.False(plan.TryPublish(in committed));
        Assert.Empty(sink.Commits);
    }

    private static ConnectionHandle Connection(ulong generation) =>
        new(GameCommandSourceId.FromConnection(100),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(generation)));

    private static ProjectileStateUpdate Update(int type) =>
        new(new ProjectileTypeId(type), 0, 100, 200, 3, 4, default, 0, 10, 2, 10);

    private sealed class Sink : IProjectileStateCommitSink
    {
        public List<ProjectileStateCommitKind> Commits { get; } = [];
        public Action<ProjectileSnapshot>? OnCommit { get; set; }

        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            Commits.Add(kind);
            OnCommit?.Invoke(snapshot);
        }
    }
}
