using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed partial class PrimeMeleeAiTests
{
    [Theory]
    [InlineData(129, 0f, 299f, 1f)]
    [InlineData(129, 0f, 599f, 0f)]
    [InlineData(129, 1f, 0f, 2f)]
    [InlineData(129, 4f, 599f, 0f)]
    [InlineData(130, 0f, 599f, 1f)]
    [InlineData(130, 0f, 599f, 0f)]
    [InlineData(130, 1f, 0f, 2f)]
    [InlineData(130, 4f, 0f, 5f)]
    public void Source_phase_handoffs_request_an_immediate_update(int type, float phase, float timer, float nextPhase)
    {
        var (_, _, stepper, arm, _) = Setup(Rows.First(row => row.GetProperty("type").GetInt32() == type));
        var before = arm with { Ai = arm.Ai with { Ai2 = phase, Ai3 = timer } };
        var proposed = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai with { Ai2 = nextPhase, Ai3 = nextPhase == 2f || nextPhase == 5f ? timer : 0f }, before.Simulation);

        Assert.True(VanillaSkeletronPrimeLimbNpcBehaviorStrategy.RequiresImmediateSync(in before, in proposed));
        Assert.True(stepper.RequiresForcedUpdate(in before, in proposed));
    }

    [Theory]
    [InlineData(129, 0f, 298f, 1f)]
    [InlineData(129, 4f, 598f, 0f)]
    [InlineData(130, 0f, 598f, 1f)]
    [InlineData(130, 4f, 0f, 4f)]
    public void Ordinary_melee_motion_stays_cadenced(int type, float phase, float timer, float nextPhase)
    {
        var (_, _, stepper, arm, _) = Setup(Rows.First(row => row.GetProperty("type").GetInt32() == type));
        var before = arm with { Ai = arm.Ai with { Ai2 = phase, Ai3 = timer } };
        var proposed = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai with { Ai2 = nextPhase, Ai3 = timer + 1f }, before.Simulation);

        Assert.False(VanillaSkeletronPrimeLimbNpcBehaviorStrategy.RequiresImmediateSync(in before, in proposed));
        Assert.False(stepper.RequiresForcedUpdate(in before, in proposed));
    }

    [Fact]
    public void Accepted_saw_and_vice_hover_boundaries_publish_forced_updates()
    {
        foreach ((int type, float timer) in new[] { (129, 299f), (130, 599f) })
        {
            var row = Rows.First(candidate => candidate.GetProperty("type").GetInt32() == type &&
                candidate.GetProperty("before").GetProperty("ai")[2].GetSingle() == 0f &&
                candidate.GetProperty("parent").GetProperty("ai")[1].GetSingle() == 0f);
            var sink = new Capture();
            var (npcs, projectiles, ai, arm, _) = Setup(row, sink);
            var update = new NpcStateUpdate(arm.Type, arm.NetId, arm.PositionX, arm.PositionY, arm.VelocityX, arm.VelocityY,
                arm.Target, arm.Ai with { Ai2 = 0f, Ai3 = timer }, arm.Simulation);
            Assert.True(npcs.TryUpdate(arm.Handle, in update, out arm));
            sink.Commits.Clear();

            Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new ArmsOnly(ai)).Applied);
            Assert.Equal(NpcStateCommitKind.ForcedUpdate, Assert.Single(sink.Commits).Kind);
            Assert.Equal(arm.Handle, sink.Commits[0].Npc.Handle);
        }
    }

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
