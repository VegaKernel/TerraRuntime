using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed partial class PrimeRangedAiTests
{
    [Theory]
    [InlineData(128, 0f, 1099f, 1f)]
    [InlineData(128, 1f, 299f, 0f)]
    [InlineData(131, 0f, 799f, 1f)]
    [InlineData(131, 3f, 799f, 4f)]
    [InlineData(131, 1f, 199f, 0f)]
    public void Source_clock_phase_boundaries_request_an_immediate_update(int type, float phase, float timer, float nextPhase)
    {
        var (_, _, stepper, arm, _) = Setup(BaseRows.First(row => row.GetProperty("type").GetInt32() == type));
        var before = arm with { Ai = arm.Ai with { Ai2 = phase, Ai3 = timer } };
        var proposed = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai with { Ai2 = nextPhase, Ai3 = 0f }, before.Simulation);

        Assert.True(VanillaPrimeRangedBehavior.RequiresImmediateSync(in before, in proposed));
        Assert.True(stepper.RequiresForcedUpdate(in before, in proposed));
    }

    [Theory]
    [InlineData(128, 0f, 1098f, 1f)]
    [InlineData(131, 0f, 798f, 1f)]
    [InlineData(131, 1f, 198f, 0f)]
    public void Ordinary_ranged_clock_updates_stay_cadenced(int type, float phase, float timer, float nextPhase)
    {
        var (_, _, stepper, arm, _) = Setup(BaseRows.First(row => row.GetProperty("type").GetInt32() == type));
        var before = arm with { Ai = arm.Ai with { Ai2 = phase, Ai3 = timer } };
        var proposed = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai with { Ai2 = nextPhase, Ai3 = timer + 1f }, before.Simulation);

        Assert.False(VanillaPrimeRangedBehavior.RequiresImmediateSync(in before, in proposed));
        Assert.False(stepper.RequiresForcedUpdate(in before, in proposed));
    }

    [Theory]
    [InlineData(128, 1f)]
    [InlineData(131, 0f)]
    public void TargetClosest_delta_forces_ranged_arm_update_unless_colliding(int type, float phase)
    {
        var (_, _, stepper, arm, _) = Setup(BaseRows.First(row => row.GetProperty("type").GetInt32() == type));
        var before = arm with
        {
            VelocityX = 5f, VelocityY = 0f, Target = 0,
            Ai = arm.Ai with { Ai2 = phase, Ai3 = 0f },
            Simulation = arm.Simulation with { DirectionX = 1, DirectionY = 1, CollideX = false, CollideY = false }
        };
        var proposed = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY, before.VelocityX,
            before.VelocityY, 1, before.Ai, before.Simulation with { DirectionX = -1, DirectionY = -1 });

        Assert.True(stepper.RequiresForcedUpdate(in before, in proposed));
        before = before with { Simulation = before.Simulation with { CollideX = true } };
        Assert.False(stepper.RequiresForcedUpdate(in before, in proposed));
    }

    [Fact]
    public void Accepted_cannon_and_laser_hover_boundaries_publish_forced_updates()
    {
        foreach ((int type, float timer) in new[] { (128, 1099f), (131, 799f) })
        {
            var row = BaseRows.First(candidate => candidate.GetProperty("type").GetInt32() == type &&
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

    public static TheoryData<int> TraceCases => new(Enumerable.Range(0, Traces.Length / 8));

    [Theory]
    [MemberData(nameof(TraceCases))]
    public void Eight_consecutive_calls_preserve_original_state_and_rng(int group)
    {
        var first = Traces[group * 8];
        var (npcs, projectiles, ai, arm, random) = Setup(first);
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        for (int step = 0; step < 8; step++)
        {
            var row = Traces[group * 8 + step];
            Assert.True(npcs.TryGet(arm.Handle, out var before)); AssertNpc(row.GetProperty("before"), before);
            random.AssertState(row.GetProperty("randomBefore"));
            Assert.Equal(1, executor.Tick(new ArmsOnly(ai)).Applied);
            Assert.True(npcs.TryGet(arm.Handle, out var after)); AssertNpc(row.GetProperty("after"), after);
            AssertProjectiles(row, projectiles, arm.Handle); random.AssertState(row.GetProperty("randomAfter"));
        }
    }

    [Theory]
    [InlineData(128)] [InlineData(131)]
    public void Repeated_speculative_planning_does_not_spend_shot_rng(int type)
    {
        var row = BaseRows.First(x => x.GetProperty("type").GetInt32() == type && x.GetProperty("projectiles").GetArrayLength() == 1);
        var (_, projectiles, ai, arm, random) = Setup(row);
        Span<NpcAiProjectileIntent> destination = stackalloc NpcAiProjectileIntent[4];
        for (int i = 0; i < 3; i++)
        {
            Assert.True(ai.TryStepState(in arm, out var proposed));
            Assert.Equal(0, ai.PlanProjectileSpawns(in arm, in proposed, destination));
        }
        random.AssertState(row.GetProperty("randomBefore")); Assert.Equal(0, projectiles.ActiveCount);
    }

    [Theory]
    [InlineData(128, false)] [InlineData(128, true)] [InlineData(131, false)] [InlineData(131, true)]
    public void Random_reentry_cannot_attach_shot_to_a_newer_source(int type, bool replacement)
    {
        var row = BaseRows.First(x => x.GetProperty("type").GetInt32() == type && x.GetProperty("projectiles").GetArrayLength() == 1);
        var (npcs, projectiles, ai, arm, random) = Setup(row);
        random.FirstDraw = () =>
        {
            Assert.True(npcs.TryGet(arm.Handle, out var current));
            var update = new NpcStateUpdate(current.Type, current.NetId, 777, current.PositionY, 0, 0, current.Target, current.Ai, current.Simulation);
            if (replacement)
            {
                Assert.True(npcs.TryDespawn(current.Handle));
                Assert.True(npcs.TrySpawn(current.Handle.Slot, in update, out _));
            }
            else Assert.True(npcs.TryUpdate(current.Handle, in update, out _));
        };
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new ArmsOnly(ai)).Applied);
        Assert.Equal(0, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory]
    [InlineData(128)] [InlineData(131)]
    public void Full_projectile_table_preserves_draws_and_resets_shot_clock(int type)
    {
        var row = BaseRows.First(x => x.GetProperty("type").GetInt32() == type && x.GetProperty("projectiles").GetArrayLength() == 1);
        var (npcs, _, ai, arm, random) = Setup(row);
        var full = new RuntimeProjectileStore(capacity: 1);
        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(full, new(VanillaProjectileIds.SkeletronPrimeBomb, 10, 20, 0, 0, 0, 0), out var occupant));
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, full).Tick(new ArmsOnly(ai)).Applied);
        Assert.True(npcs.TryGet(arm.Handle, out var after)); AssertNpc(row.GetProperty("after"), after);
        Assert.True(full.TryGet(occupant.Handle, out var unchanged)); Assert.Equal(occupant, unchanged);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory]
    [InlineData(128)] [InlineData(131)]
    public void Orphan_is_removed_at_source_threshold_without_intermediate_active_publication(int type)
    {
        var row = BaseRows.First(x => x.GetProperty("type").GetInt32() == type);
        var sink = new Capture(); var (npcs, projectiles, ai, arm, random) = Setup(row, sink);
        Assert.True(npcs.TryGetActive(0, out var parent)); Assert.True(npcs.TryDespawn(parent.Handle));
        Assert.True(npcs.TryUpdate(arm.Handle, new(arm.Type, arm.NetId, arm.PositionX, arm.PositionY,
            arm.VelocityX, arm.VelocityY, arm.Target, arm.Ai with { Ai2 = 40f }, arm.Simulation), out arm));
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        sink.Commits.Clear(); executor.Tick(new ArmsOnly(ai));
        Assert.True(npcs.TryGet(arm.Handle, out var stillActive)); Assert.Equal(50f, stillActive.Ai.Ai2);
        Assert.Equal(NpcStateCommitKind.Update, Assert.Single(sink.Commits).Kind);
        sink.Commits.Clear(); executor.Tick(new ArmsOnly(ai));
        Assert.False(npcs.TryGet(arm.Handle, out _));
        Assert.Equal(NpcStateCommitKind.Despawn, Assert.Single(sink.Commits).Kind);
        Assert.Equal(0, projectiles.ActiveCount);
        var original = Orphans.Single(x => x.GetProperty("type").GetInt32() == type &&
            x.GetProperty("parentKind").GetInt32() == 1 && x.GetProperty("phase").GetInt32() == 50);
        random.AssertState(original.GetProperty("randomAfter"));
    }

    [Theory]
    [InlineData(128, false)] [InlineData(128, true)] [InlineData(131, false)] [InlineData(131, true)]
    public void Rejected_proposal_cannot_spend_rng(int type, bool replacement)
    {
        var row = BaseRows.First(x => x.GetProperty("type").GetInt32() == type && x.GetProperty("projectiles").GetArrayLength() == 1);
        var (npcs, projectiles, ai, arm, random) = Setup(row);
        var wrapper = new ReentrantPlanner(ai, () => Supersede(npcs, arm.Handle.Slot, replacement));
        Assert.Equal(0, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(wrapper).Applied);
        Assert.Equal(0, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomBefore"));
    }

    [Theory]
    [InlineData(128, false)] [InlineData(128, true)] [InlineData(131, false)] [InlineData(131, true)]
    public void Orphan_effect_reentry_does_not_remove_newer_state(int type, bool replacement)
    {
        var row = Orphans.Single(x => x.GetProperty("type").GetInt32() == type &&
            x.GetProperty("parentKind").GetInt32() == 1 && x.GetProperty("phase").GetInt32() == 50);
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
        { next = default; return (npc.TypeIdentity == VanillaNpcIds.PrimeCannon || npc.TypeIdentity == VanillaNpcIds.PrimeLaser) && inner.TryStepState(in npc, out next); }
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
