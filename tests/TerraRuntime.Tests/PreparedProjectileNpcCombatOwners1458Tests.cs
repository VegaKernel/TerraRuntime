using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;

namespace TerraRuntime.Tests;

public sealed class PreparedCombatOwnerTests
{
    [Fact]
    public void Empty_allocation_batch_accepts_without_phantom_item_or_publication()
    {
        var sink = new ItemSink();
        var store = new RuntimeWorldItemStore(sink);
        using var preview = store.CreateAllocationPreview();
        Assert.True(preview.TryClaim());
        Assert.True(preview.ValidateOwnerFacts());
        Assert.True(preview.TryAdoptUnpublished(out var publication));
        Assert.NotNull(publication);
        Assert.Equal(0, publication.Count);
        Assert.False(publication.TryPublishNext(out _));
        Assert.Equal(0, store.ActiveCount);
        Assert.Empty(sink.Events);
    }
    [Fact]
    public void Projectile_adoption_matches_existing_reset_and_penetration_without_early_events()
    {
        foreach (short type in new short[] { 2, 34, 54 })
        {
            var sink = new ProjectileSink();
            var store = new RuntimeProjectileStore(commitSink: sink);
            var legacy = new RuntimeProjectileStore();
            var update = Shot(type);
            Assert.True(store.TrySpawn(0, in update, out var before));
            Assert.True(legacy.TrySpawn(0, in update, out var oldBefore));
            sink.Events.Clear();
            Assert.True(store.TryPrepareNpcHit(in before, out var plan));
            Assert.NotNull(plan);
            Assert.True(plan.IsCurrent);
            if (type == 34)
            {
                var reset = update with { Ai = new(-1, -1, 0) };
                Assert.True(legacy.TryUpdate(oldBefore.Handle, in reset, out _));
            }
            Assert.True(legacy.TryConsumeNpcHitPenetration(oldBefore.Handle, out bool oldDespawn, out var oldAfter));
            Assert.True(plan.TryAdoptUnpublished(out var after, out bool despawn));
            Assert.Equal(oldDespawn, despawn);
            Assert.Equal(oldAfter, after);
            Assert.Equal(legacy.ActiveCount, store.ActiveCount);
            Assert.Empty(sink.Events);
            Assert.False(plan.IsCurrent);
            Assert.False(plan.TryAdoptUnpublished(out _, out _));
            Assert.True(plan.TryPublish());
            Assert.False(plan.TryPublish());
            Assert.Equal(type == 54 ? 0 : 1, sink.Events.Count);
        }
    }

    [Fact]
    public void Projectile_capture_guards_private_lifecycle_trust_owner_and_reused_generation()
    {
        foreach (int change in new[] { 0, 1, 2, 3, 4 })
        {
            var store = new RuntimeProjectileStore();
            var update = Shot(34);
            Assert.True(store.TrySpawn(0, in update, out var before));
            if (change == 4)
                Assert.True(store.TryMarkCombatTrusted(before.Handle, new(new(0), new(1))));
            Assert.True(store.TryPrepareNpcHit(in before, out var plan));
            Assert.NotNull(plan);
            switch (change)
            {
                case 0:
                    Assert.True(store.TryConsumeNpcHitPenetration(before.Handle, out _, out _));
                    break;
                case 1:
                    Assert.True(store.TryMarkCombatTrusted(before.Handle));
                    break;
                case 2:
                    Assert.True(store.TryMarkCombatTrusted(before.Handle, new(new(0), new(1))));
                    break;
                case 4:
                    Assert.True(store.TryMarkCombatTrusted(before.Handle, new(new(0), new(2))));
                    break;
                default:
                    Assert.True(store.TryDespawn(before.Handle, out _));
                    Assert.True(store.TrySpawn(0, in update, out _));
                    break;
            }
            Assert.False(plan.IsCurrent);
            Assert.False(plan.TryAdoptUnpublished(out _, out _));
        }
    }

    [Fact]
    public void Projectile_publication_skips_replaced_terminal_slot_and_retired_live_continuation()
    {
        foreach (short type in new short[] { 2, 34 })
        {
            var sink = new ProjectileSink();
            var store = new RuntimeProjectileStore(commitSink: sink);
            var update = Shot(type);
            Assert.True(store.TrySpawn(0, in update, out var before));
            Assert.True(store.TryPrepareNpcHit(in before, out var plan));
            Assert.NotNull(plan);
            Assert.True(plan.TryAdoptUnpublished(out _, out bool despawn));
            if (!despawn) Assert.True(store.TryDespawn(before.Handle, out _));
            Assert.True(store.TrySpawn(0, in update, out var replacement));
            sink.Events.Clear();
            Assert.False(plan.TryPublish());
            Assert.False(plan.TryPublish());
            Assert.Empty(sink.Events);
            Assert.True(store.TryGet(replacement.Handle, out var retained));
            Assert.Equal(replacement, retained);
        }
    }

    [Fact]
    public void Projectile_reset_callback_replacement_skips_following_despawn()
    {
        var sink = new ProjectileSink();
        var store = new RuntimeProjectileStore(commitSink: sink);
        var update = Shot(34);
        Assert.True(store.TrySpawn(0, in update, out var birth));
        Assert.True(store.TryCommitSimulationStep(birth.Handle, in update, 100, null, null,
            out var before, out _, penetrateOverride: 1));
        Assert.True(store.TryPrepareNpcHit(in before, out var plan));
        Assert.NotNull(plan);
        Assert.True(plan.TryAdoptUnpublished(out _, out bool despawn));
        Assert.True(despawn);
        sink.Events.Clear();
        ProjectileSnapshot replacement = default;
        sink.Callback = () =>
        {
            sink.Callback = null;
            Assert.False(plan.TryPublish());
            Assert.True(store.TrySpawn(0, in update, out replacement));
        };
        Assert.True(plan.TryPublish());
        Assert.DoesNotContain(ProjectileStateCommitKind.Despawn, sink.Events);
        Assert.True(store.TryGet(replacement.Handle, out var retained));
        Assert.Equal(replacement, retained);
    }

    [Fact]
    public void Allocation_adopts_all_items_releases_claims_then_publishes_once_without_overwrite()
    {
        var sink = new ItemSink();
        var store = new RuntimeWorldItemStore(sink);
        using var preview = store.CreateAllocationPreview();
        var drop = Drop();
        Assert.True(preview.TrySpawnSource(in drop, 0, out short first));
        Assert.True(preview.TrySpawnSource(in drop, 0, out short second));
        Assert.True(preview.TryClaim());
        Assert.True(preview.ValidateOwnerFacts());
        Assert.True(preview.IsCurrentOwned);
        Assert.True(preview.TryAdoptUnpublished(out var publication));
        Assert.NotNull(publication);
        Assert.Equal(2, store.ActiveCount);
        Assert.Empty(sink.Events);
        Assert.False(store.IsAllocationClaimed(first));
        Assert.False(store.IsAllocationClaimed(second));
        sink.Callback = () =>
        {
            Assert.False(publication.TryPublishNext(out _));
            Assert.True(store.TryApplyDrop(first, drop with { Stack = 99 }, out _));
            sink.Callback = null;
        };
        // Disable the callback before its nested store publication.
        sink.Once = true;
        Assert.True(publication.TryPublishNext(out short published));
        Assert.Equal(first, published);
        Assert.True(publication.TryPublishNext(out published));
        Assert.Equal(second, published);
        Assert.False(publication.TryPublishNext(out _));
        Assert.True(store.TryGetActive(first, out var retained));
        Assert.Equal(99, retained.Stack);
        Assert.False(preview.TryAdoptUnpublished(out _));
    }

    [Fact]
    public void Allocation_stale_claim_rejects_every_operation_and_preserves_external_change()
    {
        foreach (bool claimFirst in new[] { false, true })
        {
            var store = new RuntimeWorldItemStore();
            using var preview = store.CreateAllocationPreview();
            var drop = Drop();
            Assert.True(preview.TrySpawnSource(in drop, 0, out _));
            if (claimFirst) Assert.True(preview.TryClaim());
            Assert.True(store.TryAllocateDrop(in drop, out var outside));
            if (!claimFirst) Assert.False(preview.TryClaim());
            Assert.False(preview.TryAdoptUnpublished(out _));
            Assert.Equal(1, store.ActiveCount);
            Assert.True(store.TryGetActive(outside.Handle.Slot, out var retained));
            Assert.Equal(outside, retained);
        }
    }

    [Fact]
    public void Allocation_preserves_source_removal_reuse_and_instanced_lease_token()
    {
        var sink = new ItemSink();
        var store = new RuntimeWorldItemStore(sink);
        var drop = Drop();
        Assert.True(store.TryAllocateDrop(in drop, out var original));
        sink.Events.Clear();
        using var preview = store.CreateAllocationPreview();
        Assert.True(preview.TryRemoveSource(in original));
        Assert.True(preview.TrySpawnSource(in drop, 60, out short slot));
        Assert.Equal(original.Handle.Slot, slot);
        Assert.True(preview.TryClaim());
        Assert.True(preview.ValidateOwnerFacts());
        Assert.True(preview.TryAdoptUnpublished(out var publication));
        Assert.NotNull(publication);
        Assert.True(publication.TryGetLease(0, out var none));
        Assert.False(none.IsAssigned);
        Assert.True(publication.TryGetLease(1, out var lease));
        Assert.True(lease.IsAssigned);
        Assert.NotEqual(original.Handle.Generation, lease.Generation);
        Assert.True(store.HasDropReservation(in lease));
        Assert.Empty(sink.Events);
        Assert.True(publication.TryPublishNext(out _));
        Assert.Single(sink.Events);
        Assert.Equal(WorldItemStateCommitKind.Remove, sink.Events[0].Kind);
        Assert.Equal(original, sink.Events[0].Snapshot);
        Assert.True(publication.TryPublishNext(out _));
        Assert.False(publication.TryPublishNext(out _));
    }

    [Fact]
    public void Allocation_preserves_transient_sentinel_without_physical_generation_or_replay()
    {
        var sink = new ItemSink();
        var store = new RuntimeWorldItemStore(sink);
        var full = Drop() with { ItemNetId = 22, Stack = 9999 };
        for (int index = 0; index < RuntimeWorldItemStore.VanillaCapacity; index++)
            Assert.True(store.TryAllocateDrop(in full, out _));
        sink.Events.Clear();
        using var preview = store.CreateAllocationPreview();
        var drop = Drop();
        Assert.True(preview.TrySpawnSource(in drop, 0, out short slot));
        Assert.Equal(400, slot);
        Assert.True(preview.TryClaim());
        Assert.True(preview.ValidateOwnerFacts());
        Assert.True(preview.TryAdoptUnpublished(out var publication));
        Assert.NotNull(publication);
        Assert.Equal(400, store.ActiveCount);
        Assert.True(publication.TryPublishNext(out slot));
        Assert.Equal(400, slot);
        Assert.Single(sink.Sentinels);
        Assert.Empty(sink.Events);
        Assert.False(publication.TryPublishNext(out _));
    }

    [Fact]
    public void Allocation_emergency_transfers_match_incremental_source_owner_and_keep_ordered_snapshots()
    {
        var sink = new ItemSink();
        var store = new RuntimeWorldItemStore(sink);
        var incremental = new RuntimeWorldItemStore();
        var drop = Drop();
        for (int index = 0; index < RuntimeWorldItemStore.VanillaCapacity; index++)
        {
            Assert.True(store.TryAllocateDrop(in drop, out _));
            Assert.True(incremental.TryAllocateDrop(in drop, out _));
        }
        sink.Events.Clear();
        using var preview = store.CreateAllocationPreview();
        for (int index = 0; index < 2; index++)
        {
            Assert.True(preview.TrySpawnSource(in drop, 0, out short staged));
            Assert.True(incremental.TryAllocateSourceDrop(in drop, default, out _, out short direct));
            Assert.Equal(direct, staged);
        }
        Assert.True(preview.TryClaim());
        Assert.True(preview.ValidateOwnerFacts());
        Assert.True(preview.TryAdoptUnpublished(out var publication));
        Assert.NotNull(publication);
        Assert.Empty(sink.Events);
        Assert.Equal(incremental.ActiveCount, store.ActiveCount);
        for (short slot = 0; slot < RuntimeWorldItemStore.VanillaCapacity; slot++)
        {
            bool active = store.TryGetActive(slot, out var actual);
            Assert.Equal(incremental.TryGetActive(slot, out var expected), active);
            if (active) Assert.Equal(expected, actual);
            Assert.True(store.TryGetAllocationMetadata(slot, out int age, out int reuse));
            Assert.True(incremental.TryGetAllocationMetadata(slot, out int oldAge, out int oldReuse));
            Assert.Equal(oldAge, age);
            Assert.Equal(oldReuse, reuse);
        }
        Assert.True(publication.TryPublishNext(out _));
        Assert.True(publication.TryPublishNext(out _));
        Assert.False(publication.TryPublishNext(out _));
        Assert.Contains(sink.Events, entry => entry.Kind == WorldItemStateCommitKind.Remove);
    }

    [Fact]
    public void Allocation_final_adoption_and_publication_do_not_reenter_external_owner_provider()
    {
        var provider = new OwnerFacts();
        var store = new RuntimeWorldItemStore();
        store.AttachOwnerFactsProvider(provider);
        using var preview = store.CreateAllocationPreview();
        var drop = Drop();
        Assert.True(preview.TrySpawnSource(in drop, 0, out _));
        Assert.True(preview.TryClaim());
        Assert.True(preview.ValidateOwnerFacts());
        int calls = provider.Calls;
        provider.RejectQueries = true;
        // External validation is the caller's precommit phase, followed by the pure store guard.
        Assert.True(preview.IsCurrentOwned);
        Assert.True(preview.TryAdoptUnpublished(out var publication));
        Assert.NotNull(publication);
        Assert.Equal(calls, provider.Calls);
        Assert.True(publication.TryPublishNext(out _));
        Assert.Equal(calls, provider.Calls);
    }

    [Fact]
    public void Allocation_reentrant_drop_change_skips_stale_owner_event_without_restoring_claims()
    {
        var sink = new ItemSink();
        var store = new RuntimeWorldItemStore(sink);
        store.AttachOwnerFactsProvider(new OwnerFacts { SelectedOwner = 0 });
        using var preview = store.CreateAllocationPreview();
        var drop = Drop();
        Assert.True(preview.TrySpawnSource(in drop, 0, out short slot));
        Assert.True(preview.TryClaim());
        Assert.True(preview.ValidateOwnerFacts());
        Assert.True(preview.TryAdoptUnpublished(out var publication));
        Assert.NotNull(publication);
        sink.Once = true;
        sink.Callback = () => Assert.True(store.TryApplyDrop(slot, drop with { Stack = 99 }, out _));
        Assert.True(publication.TryPublishNext(out _));
        Assert.DoesNotContain(sink.Events, entry => entry.Kind == WorldItemStateCommitKind.Owner);
        Assert.True(store.TryGetActive(slot, out var retained));
        Assert.Equal(99, retained.Stack);
        Assert.Equal(0, retained.OwnerPlayerId);
        Assert.False(store.IsAllocationClaimed(slot));
    }

    private sealed class OwnerFacts : IWorldItemOwnerFactsProvider1458, IWorldItemOwnerFactsSnapshot1458
    {
        public int Calls;
        public bool RejectQueries;
        public byte SelectedOwner = byte.MaxValue;
        public IWorldItemOwnerFactsSnapshot1458 Capture() => this;
        public bool IsCurrent
        {
            get
            {
                Assert.False(RejectQueries);
                Calls++;
                return true;
            }
        }
        public bool TrySelectOwner(in WorldItemDropStateUpdate drop, int grabDelay, byte grabPlayer, out byte owner)
        {
            owner = SelectedOwner;
            return true;
        }
    }

    private static ProjectileStateUpdate Shot(short type) => new(new(type), 0, 800, 800, 3, 0,
        new(-1, 9, 0), 0, 10, 1, 10);

    private static WorldItemDropStateUpdate Drop() => new(800, 800, 0, 0, 1, 0,
        WorldItemOwnershipMode.None, 2, false, 0, 0);

    private sealed class ProjectileSink : IProjectileStateCommitSink
    {
        public readonly List<ProjectileStateCommitKind> Events = new();
        public Action? Callback;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            Events.Add(kind);
            Callback?.Invoke();
        }
    }

    private sealed class ItemSink : IWorldItemStateCommitSink, IWorldItemSentinelCommitSink1458
    {
        public readonly List<(WorldItemStateCommitKind Kind, WorldItemSnapshot Snapshot)> Events = new();
        public readonly List<WorldItemSentinelCommit1458> Sentinels = new();
        public Action? Callback;
        public bool Once;
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            Events.Add((kind, snapshot));
            var callback = Callback;
            if (Once) Callback = null;
            callback?.Invoke();
        }
        public void WorldItemSentinelCommitted(in WorldItemSentinelCommit1458 commit) => Sentinels.Add(commit);
    }
}
