using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeNpcAiStateExecutorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Earlier_slot_changes_are_visible_when_later_slot_runs(bool replace)
    {
        var store = new RuntimeNpcStore(4);
        var first = CreateUpdate(1, 1, 10f, 0f);
        var second = CreateUpdate(2, 2, 20f, 0f);
        Assert.True(store.TrySpawn(0, in first, out _));
        Assert.True(store.TrySpawn(2, in second, out _));
        var stepper = new LiveSlotStepper(store, replace, spawn: false);

        Assert.Equal(new NpcAiStateTickSummary(2, 2, 2, 0), new RuntimeNpcAiStateExecutor(store).Tick(stepper));

        Assert.Equal(new byte[] { 0, 2 }, stepper.Visited);
        Assert.True(store.TryGetActive(2, out var current));
        Assert.Equal(501f, current.PositionX);
        Assert.Equal(replace ? 99 : 2, current.Type);
        Assert.Equal(11f, stepper.EarlierPeerX);
    }

    [Fact]
    public void Newly_spawned_higher_slot_runs_now_but_lower_slot_waits_until_next_tick()
    {
        var store = new RuntimeNpcStore(4);
        var first = CreateUpdate(1, 1, 10f, 0f);
        Assert.True(store.TrySpawn(1, in first, out _));
        var stepper = new LiveSlotStepper(store, replace: false, spawn: true);
        var executor = new RuntimeNpcAiStateExecutor(store);

        Assert.Equal(new NpcAiStateTickSummary(2, 2, 2, 0), executor.Tick(stepper));
        Assert.Equal(new byte[] { 1, 2 }, stepper.Visited);
        Assert.True(store.TryGetActive(0, out var lower));
        Assert.Equal(500f, lower.PositionX);
        Assert.True(store.TryGetActive(2, out var higher));
        Assert.Equal(501f, higher.PositionX);

        stepper.Visited.Clear();
        Assert.Equal(new NpcAiStateTickSummary(3, 3, 3, 0), executor.Tick(stepper));
        Assert.Equal(new byte[] { 0, 1, 2 }, stepper.Visited);
        Assert.True(store.TryGetActive(0, out lower));
        Assert.Equal(501f, lower.PositionX);
    }

    private sealed class LiveSlotStepper(RuntimeNpcStore store, bool replace, bool spawn)
        : INpcAiStateStepper, INpcAiPeerSnapshotConsumer
    {
        private readonly IncrementStateStepper inner = new();
        private bool changed;
        private NpcSnapshot[] peers = [];
        public List<byte> Visited { get; } = [];
        public float EarlierPeerX { get; private set; }

        public void SetNpcPeers(ReadOnlySpan<NpcSnapshot> values) => peers = values.ToArray();

        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            Visited.Add(npc.Handle.Slot);
            if (!changed)
            {
                changed = true;
                var mutation = CreateUpdate(replace ? 99 : 2, (short)(replace ? 99 : 2), 500f, 0f);
                if (spawn)
                {
                    Assert.True(store.TrySpawn(0, in mutation, out _));
                    Assert.True(store.TrySpawn(2, in mutation, out _));
                }
                else
                {
                    Assert.True(store.TryGetActive(2, out var target));
                    if (replace)
                    {
                        Assert.True(store.TryDespawn(target.Handle));
                        Assert.True(store.TrySpawn(2, in mutation, out _));
                    }
                    else Assert.True(store.TryUpdate(target.Handle, in mutation, out _));
                }
            }
            else if (npc.Handle.Slot == 2)
                EarlierPeerX = peers.First(p => p.Type == 1).PositionX;
            return inner.TryStepState(in npc, out next);
        }
    }
}
