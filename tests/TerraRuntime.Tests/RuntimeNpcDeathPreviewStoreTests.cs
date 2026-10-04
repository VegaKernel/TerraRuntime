using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcDeathPreviewStoreTests
{
    [Fact]
    public void Preview_preserves_active_revisions_and_retained_slots_without_sharing_mutations()
    {
        var sink = new RecordingSink();
        var owner = new RuntimeNpcStore(3, sink);
        var state = Bunny(123.25f);
        Assert.True(owner.TrySpawn(1, in state, out var first));
        var moved = state with { PositionX = 144.5f };
        Assert.True(owner.TryUpdate(first.Handle, in moved, out var current));
        Assert.True(owner.TrySpawn(2, in state, out var removed));
        Assert.True(owner.TryDespawn(removed.Handle));
        int publications = sink.Count;

        var preview = owner.CreateDeathPreview(new SystemVanillaNpcRandom(0));
        Assert.Equal(owner.Capacity, preview.Capacity);
        Assert.Equal(owner.ActiveCount, preview.ActiveCount);
        Assert.True(preview.TryGet(current.Handle, out var copied));
        Assert.Equal(current, copied);
        var originalSlots = new VanillaNpcRetainedSlot[3];
        var copiedSlots = new VanillaNpcRetainedSlot[3];
        owner.CopyRetainedSlots(originalSlots);
        preview.CopyRetainedSlots(copiedSlots);
        Assert.Equal(originalSlots, copiedSlots);

        Assert.True(preview.TryDespawn(current.Handle));
        Assert.True(preview.TrySpawn(1, in state, out var replacement));
        Assert.NotEqual(current.Handle.Generation, replacement.Handle.Generation);
        Assert.False(preview.TryGet(current.Handle, out _));
        Assert.True(owner.TryGet(current.Handle, out var unchanged));
        Assert.Equal(current, unchanged);
        Assert.Equal(publications, sink.Count);
        Assert.False(owner.TryGetActive(2, out _));
        Assert.True(preview.TrySpawn(2, in state, out _));
        Assert.False(owner.TryGetActive(2, out _));
    }

    [Fact]
    public void Preview_retains_inactive_spawn_protection_and_expiry()
    {
        var owner = new RuntimeNpcStore(2);
        var state = Bunny(100);
        Assert.True(owner.TrySpawnVanilla(in state, out var protectedNpc));
        Assert.Equal(0, protectedNpc.Handle.Slot);
        Assert.True(owner.TryDespawn(protectedNpc.Handle));
        var preview = owner.CreateDeathPreview(new SystemVanillaNpcRandom(0));
        Assert.True(preview.TrySpawnVanilla(in state, out var next));
        Assert.Equal(1, next.Handle.Slot);
        Assert.True(preview.TryDespawn(next.Handle));
        Assert.False(preview.TrySpawnVanilla(in state, out _));
        for (int i = 0; i < VanillaNpcSpawnRules.SpawnProtectionUpdates; i++)
            preview.UpdateProtectedSpawnSlots();
        Assert.True(preview.TrySpawnVanilla(in state, out var expired));
        Assert.Equal(0, expired.Handle.Slot);
        Assert.True(owner.TrySpawnVanilla(in state, out var originalNext));
        Assert.Equal(1, originalNext.Handle.Slot);
    }

    [Fact]
    public void Preview_samples_context_once_and_uses_only_supplied_random()
    {
        var originalRandom = new SystemVanillaNpcRandom(1458);
        var before = originalRandom.SourceRandom.Clone();
        var previewRandom = new SystemVanillaNpcRandom(0);
        var owner = new RuntimeNpcStore(3);
        owner.SetVanillaSpawnRandomSource(originalRandom);
        Assert.True(owner.IsSpawnRandomSource(originalRandom.SourceRandom));
        Assert.False(owner.IsSpawnRandomSource(previewRandom.SourceRandom));
        Assert.False(owner.IsSpawnRandomSource(new VanillaUnifiedRandom1458(1458)));
        int calls = 0;
        var context = new VanillaNpcSpawnContext(1, 1, true);
        owner.SetVanillaSpawnContextSource(() => { calls++; return context; });

        var preview = owner.CreateDeathPreview(previewRandom);
        Assert.True(preview.HasGoodWorldSpawnContext);
        Assert.Equal(1, calls);
        context = context with { GoodWorld = false };
        var state = Bunny(100);
        Assert.True(preview.TrySpawnVanilla(in state, out var first));
        Assert.True(preview.TrySpawnVanilla(in state, out var second));
        Assert.Equal(VanillaNpcIds.ExplosiveBunny.Value, first.Type);
        Assert.Equal(VanillaNpcIds.ExplosiveBunny.Value, second.Type);
        Assert.Equal(1, calls);
        Assert.True(originalRandom.SourceRandom.HasSameState(before));
        Assert.Equal(0, owner.ActiveCount);
    }

    [Fact]
    public void Preview_rejects_missing_random_before_sampling_live_context()
    {
        var owner = new RuntimeNpcStore();
        int calls = 0;
        owner.SetVanillaSpawnContextSource(() => { calls++; return new(1, 1, false); });
        Assert.Throws<ArgumentNullException>(() => owner.CreateDeathPreview(null!));
        Assert.Equal(0, calls);
        Assert.Throws<ArgumentNullException>(() => owner.IsSpawnRandomSource(null!));
        Assert.False(owner.CreateDeathPreview(new SystemVanillaNpcRandom(0)).HasGoodWorldSpawnContext);
    }

    private static NpcStateUpdate Bunny(float x) => new(46, 46, x, 100, 0, 0,
        VanillaNpcDefinitionCatalog.DefaultTarget, default, NpcSimulationState.Initial);

    private sealed class RecordingSink : INpcStateCommitSink
    {
        public int Count { get; private set; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Count++;
    }
}
