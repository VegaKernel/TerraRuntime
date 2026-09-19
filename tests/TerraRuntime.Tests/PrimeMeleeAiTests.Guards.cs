using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed partial class PrimeMeleeAiTests
{
    [Theory]
    [InlineData(129, false)] [InlineData(129, true)] [InlineData(130, false)] [InlineData(130, true)]
    public void Rejected_proposal_cannot_spend_rng(int type, bool replacement)
    {
        var row = Rows.First(x => x.GetProperty("type").GetInt32() == type && !x.GetProperty("after").GetProperty("active").GetBoolean());
        var (npcs, projectiles, ai, arm, random) = Setup(row);
        var wrapper = new ReentrantPlanner(ai, () => Supersede(npcs, arm.Handle.Slot, replacement));
        Assert.Equal(0, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(wrapper).Applied);
        Assert.Equal(0, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomBefore"));
    }

    [Theory]
    [InlineData(129, false)] [InlineData(129, true)] [InlineData(130, false)] [InlineData(130, true)]
    public void Orphan_effect_reentry_does_not_remove_newer_state(int type, bool replacement)
    {
        var row = Rows.Single(x => x.GetProperty("type").GetInt32() == type &&
            x.TryGetProperty("parentKind", out var kind) && kind.GetInt32() == 2 && x.GetProperty("phase").GetInt32() == 50 && x.GetProperty("geometry").GetInt32() == 0);
        var (npcs, projectiles, ai, arm, random) = Setup(row);
        random.FirstDraw = () => Supersede(npcs, arm.Handle.Slot, replacement);
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new ArmsOnly(ai));
        Assert.True(npcs.TryGetActive(arm.Handle.Slot, out var surviving));
        Assert.Equal(321, surviving.Simulation.Life);
        Assert.Equal(777f, surviving.PositionX);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    private static void Supersede(RuntimeNpcStore npcs, byte slot, bool replacement)
    {
        Assert.True(npcs.TryGetActive(slot, out var current));
        var update = new NpcStateUpdate(current.Type, current.NetId, 777, current.PositionY, 0, 0,
            current.Target, current.Ai, current.Simulation with { Life = 321 });
        if (replacement)
        {
            Assert.True(npcs.TryDespawn(current.Handle));
            Assert.True(npcs.TrySpawn(slot, in update, out _));
        }
        else Assert.True(npcs.TryUpdate(current.Handle, in update, out _));
    }

    private sealed class ReentrantPlanner(INpcAiStateStepper inner, Action mutation) :
        INpcAiStateStepper, INpcAiStateStepperWrapper, INpcAiProjectileIntentPlanner
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { next = default; return (npc.TypeIdentity == VanillaNpcIds.PrimeSaw || npc.TypeIdentity == VanillaNpcIds.PrimeVice) && inner.TryStepState(in npc, out next); }
        public int PlanProjectileSpawns(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
        {
            int count = ((INpcAiProjectileIntentPlanner)inner).PlanProjectileSpawns(in source, in proposed, destination);
            mutation(); return count;
        }
    }

    private sealed class Capture : INpcStateCommitSink
    {
        public List<(NpcStateCommitKind Kind, NpcSnapshot Npc)> Commits { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Commits.Add((kind, npc));
    }
}
