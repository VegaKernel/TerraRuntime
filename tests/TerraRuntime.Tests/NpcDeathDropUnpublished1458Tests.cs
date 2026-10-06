using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class NpcDeathDropUnpublished1458Tests
{
    [Fact]
    public void Entire_drop_batch_and_final_random_are_adopted_before_callbacks_without_later_rewind()
    {
        var source = new VanillaUnifiedRandom1458(1458);
        var sink = new Sink();
        var items = new RuntimeWorldItemStore(sink);
        using var plan = Prepare(items, source, out var npc);
        Assert.True(plan.TryEnableUnpublished(in npc));
        Assert.True(plan.ValidateUnpublishedOwnerFacts());
        var expected = plan.Random.Clone();
        Assert.Equal(0, items.ActiveCount);
        Assert.True(plan.CanAdoptUnpublished());
        Assert.True(plan.TryAdoptUnpublished());
        Assert.False(plan.TryAdoptUnpublished());
        Assert.Equal(2, items.ActiveCount);
        Assert.Empty(sink.Frames);
        Assert.True(source.HasSameState(expected));
        bool entered = false;
        sink.OnCommit = () =>
        {
            Assert.Equal(2, items.ActiveCount);
            if (!entered)
            {
                entered = true;
                Assert.True(source.HasSameState(expected));
                Assert.Equal(expected.Next(), source.Next());
                Assert.False(plan.TryPublishPhase(NpcDeathDropPhase1458.Imported, (_, _, _, _) => false));
                Assert.False(plan.TryPublishPhase(NpcDeathDropPhase1458.Recovery, (_, _, _, _) => false));
            }
        };
        PublishAll(plan);
        Assert.True(source.HasSameState(expected));
        Assert.Equal(new short[] { 2, 3 }, sink.Frames.Where(x => x.Kind == WorldItemStateCommitKind.Drop)
            .Select(x => x.NpcItem));
        Assert.False(plan.TryPublishPhase(NpcDeathDropPhase1458.Healing, (_, _, _, _) => false));
    }

    [Fact]
    public void Changed_live_random_or_allocation_rejects_before_adoption()
    {
        var source = new VanillaUnifiedRandom1458(1458);
        var items = new RuntimeWorldItemStore();
        using var plan = Prepare(items, source, out var npc);
        Assert.True(plan.TryEnableUnpublished(in npc));
        source.Next();
        var changed = source.Clone();
        Assert.False(plan.CanAdoptUnpublished());
        Assert.False(plan.TryAdoptUnpublished());
        Assert.Equal(0, items.ActiveCount);
        Assert.True(source.HasSameState(changed));

        var secondSource = new VanillaUnifiedRandom1458(0);
        var secondItems = new RuntimeWorldItemStore();
        using var second = Prepare(secondItems, secondSource, out npc);
        Assert.True(second.TryEnableUnpublished(in npc));
        var unrelated = new WorldItemStateUpdate(900, 900, 0, 0, 1, 0, WorldItemOwnershipMode.None,
            4, false, 0, 0, byte.MaxValue, 0, byte.MaxValue, 0);
        Assert.True(secondItems.TryAllocate(in unrelated, out _));
        Assert.False(second.CanAdoptUnpublished());
        Assert.False(second.TryAdoptUnpublished());
        Assert.Equal(1, secondItems.ActiveCount);
    }

    [Fact]
    public void Empty_loot_result_still_adopts_final_random_once_and_has_no_publication()
    {
        var source = new VanillaUnifiedRandom1458(0);
        var sink = new Sink();
        var items = new RuntimeWorldItemStore(sink);
        using var plan = Prepare(items, source, out var npc, empty: true);
        Assert.True(plan.TryEnableUnpublished(in npc));
        var expected = plan.Random.Clone();
        Assert.True(plan.TryAdoptUnpublished());
        PublishAll(plan);
        Assert.Equal(0, items.ActiveCount);
        Assert.Empty(sink.Frames);
        Assert.True(source.HasSameState(expected));
    }

    [Fact]
    public void Positive_instanced_lease_and_uncaptured_canonical_owner_are_fenced_before_hp()
    {
        var source = new VanillaUnifiedRandom1458(1458);
        var items = new RuntimeWorldItemStore();
        using var plan = Prepare(items, source, out var npc, leaseTicks: 60);
        var expected = source.Clone();
        Assert.False(plan.TryEnableUnpublished(in npc));
        Assert.False(plan.CanAdoptUnpublished());
        Assert.False(plan.TryAdoptUnpublished());
        Assert.True(source.HasSameState(expected));
        Assert.Equal(0, items.ActiveCount);

        using var other = Prepare(new RuntimeWorldItemStore(), new VanillaUnifiedRandom1458(0), out npc,
            type: VanillaNpcIds.Skeleton);
        Assert.False(other.TryEnableUnpublished(in npc));
    }

    [Fact]
    public void Existing_incremental_phase_publication_keeps_its_checkpoint_semantics()
    {
        var source = new VanillaUnifiedRandom1458(1458);
        var items = new RuntimeWorldItemStore();
        using var plan = Prepare(items, source, out _);
        Assert.False(plan.IsUnpublishedMode);
        Assert.True(plan.TryPublishPhase(NpcDeathDropPhase1458.Prelude, (_, _, _, _) => false));
        Assert.Equal(0, items.ActiveCount);
        Assert.True(plan.TryPublishPhase(NpcDeathDropPhase1458.Imported, (_, _, _, _) => false));
        Assert.Equal(1, items.ActiveCount);
        Assert.True(plan.TryPublishPhase(NpcDeathDropPhase1458.Recovery, (_, _, _, _) => false));
        Assert.True(plan.TryPublishPhase(NpcDeathDropPhase1458.Money, (_, _, _, _) => false));
        Assert.Equal(2, items.ActiveCount);
        Assert.True(plan.TryPublishPhase(NpcDeathDropPhase1458.Healing, (_, _, _, _) => false));
        Assert.True(source.HasSameState(plan.Random));
    }

    private static RuntimeNpcDeathDropPlan1458 Prepare(RuntimeWorldItemStore items, VanillaUnifiedRandom1458 source,
        out NpcSnapshot npc, bool empty = false, int leaseTicks = 0, NpcTypeId? type = null)
    {
        var actors = new RuntimeNpcStore();
        NpcTypeId identity = type ?? VanillaNpcIds.Zombie;
        Assert.True(actors.TrySpawn(0, new(identity.Value, checked((short)identity.Value), 800, 800, 0, 0,
            255, default, NpcSimulationState.Initial with { Life = 45, LifeMax = 45 }), out npc));
        var plan = new RuntimeNpcDeathDropPlan1458(npc.Handle, npc.Revision, source);
        foreach (var phase in Enum.GetValues<NpcDeathDropPhase1458>())
        {
            Assert.True(plan.BeginPreviewPhase(phase));
            plan.Random.Next();
            if (!empty && phase is NpcDeathDropPhase1458.Imported or NpcDeathDropPhase1458.Money)
            {
                var drop = new WorldItemDropStateUpdate(800, 800, 0, 0, 1, 0, WorldItemOwnershipMode.None,
                    phase == NpcDeathDropPhase1458.Imported ? (short)2 : (short)3, false, 0, 0);
                Assert.True(plan.TryStage(phase, in drop, leaseTicks: leaseTicks));
            }
            Assert.True(plan.FinishPreviewPhase(phase));
        }
        Assert.True(plan.TryReserve(items));
        Assert.True(plan.TryAccept((_, _) => true));
        return plan;
    }

    private static void PublishAll(RuntimeNpcDeathDropPlan1458 plan)
    {
        foreach (var phase in Enum.GetValues<NpcDeathDropPhase1458>())
            Assert.True(plan.TryPublishPhase(phase, (_, _, _, _) => false));
    }

    private sealed class Sink : IWorldItemStateCommitSink
    {
        internal readonly List<(WorldItemStateCommitKind Kind, short NpcItem)> Frames = [];
        internal Action? OnCommit;
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            Frames.Add((kind, snapshot.ItemNetId));
            OnCommit?.Invoke();
        }
    }
}
