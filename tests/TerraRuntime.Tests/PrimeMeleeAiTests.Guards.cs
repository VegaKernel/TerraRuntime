using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed partial class PrimeMeleeAiTests
{
    [Fact]
    public void Charge_transition_uses_the_target_live_hitbox_top_edge()
    {
        JsonElement row = Rows.First(x => x.GetProperty("type").GetInt32() == VanillaNpcIds.PrimeSaw.Value &&
            x.GetProperty("phase").GetInt32() == 2 && x.GetProperty("geometry").GetInt32() == 0);

        Assert.Equal(2f, StepCharge(row, hitboxHeight: 0f));
        Assert.Equal(3f, StepCharge(row, hitboxHeight: 76f));
    }

    private static float StepCharge(JsonElement row, float hitboxHeight)
    {
        var (npcs, projectiles, ai, arm, _) = Setup(row);
        var update = new NpcStateUpdate(arm.Type, arm.NetId, arm.PositionX, 1015f, arm.VelocityX, 1f,
            arm.Target, arm.Ai with { Ai2 = 2f }, arm.Simulation);
        Assert.True(npcs.TryUpdate(arm.Handle, in update, out _));
        ai.SetCandidates([new VanillaNpcTargetCandidate(0, 1510f, 1050f, 0, true, false, false, false)
        {
            HitboxHeight = hitboxHeight
        }]);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new ArmsOnly(ai)).Applied);
        Assert.True(npcs.TryGet(arm.Handle, out var after));
        return after.Ai.Ai2;
    }

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
