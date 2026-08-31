using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcAiProjectileIntentTests
{
    [Fact]
    public void Projectile_intent_is_allocated_only_after_source_commit()
    {
        var npcs = new RuntimeNpcStore(capacity: 2);
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        NpcStateUpdate initial = CreateNpcUpdate(type: 1, positionX: 10f);
        Assert.True(npcs.TrySpawn(0, in initial, out NpcSnapshot source));
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);

        NpcAiStateTickSummary summary = executor.Tick(new ProjectilePlanningStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(11f, committed.PositionX);
        Assert.Equal(1, projectiles.ActiveCount);
        var snapshots = new ProjectileSnapshot[1];
        Assert.Equal(1, projectiles.CopyActive(snapshots));
        Assert.Equal(VanillaProjectileIds.ProbePinkLaser, snapshots[0].Type);
        Assert.Equal(byte.MaxValue, snapshots[0].Spawner);
        Assert.Equal((short)25, snapshots[0].Damage);
        Assert.Equal((short)25, snapshots[0].OriginalDamage);
    }

    [Fact]
    public void Stale_source_commit_cannot_publish_ghost_projectile()
    {
        var npcs = new RuntimeNpcStore(capacity: 2);
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        NpcStateUpdate initial = CreateNpcUpdate(type: 1, positionX: 10f);
        Assert.True(npcs.TrySpawn(0, in initial, out NpcSnapshot source));
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var stepper = new ReplacingProjectilePlanningStepper(npcs);

        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 0, 1), summary);
        Assert.Equal(0, projectiles.ActiveCount);
        Assert.False(npcs.TryGet(source.Handle, out _));
        Assert.True(npcs.TryGetActive(0, out NpcSnapshot replacement));
        Assert.Equal(99, replacement.Type);
    }

    [Fact]
    public void Intent_applier_rejects_invalid_damage_without_allocating()
    {
        var projectiles = new RuntimeProjectileStore(capacity: 2);
        var intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.ProbePinkLaser,
            0f,
            0f,
            1f,
            0f,
            short.MaxValue + 1,
            0f);

        Assert.False(RuntimeNpcProjectileIntentApplier.TryApply(projectiles, in intent, out _));
        Assert.Equal(0, projectiles.ActiveCount);
    }

    private static NpcStateUpdate CreateNpcUpdate(int type, float positionX) =>
        new(
            type,
            checked((short)type),
            positionX,
            20f,
            0f,
            0f,
            Target: 0,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

    private class ProjectilePlanningStepper : INpcAiStateStepper, INpcAiProjectileIntentPlanner
    {
        public virtual bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX + 1f,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }

        public virtual int PlanProjectileSpawns(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiProjectileIntent> destination)
        {
            destination[0] = new NpcAiProjectileIntent(
                VanillaProjectileIds.ProbePinkLaser,
                source.PositionX,
                source.PositionY,
                6f,
                0f,
                25,
                0f);
            return 1;
        }
    }

    private sealed class ReplacingProjectilePlanningStepper(RuntimeNpcStore npcs) : ProjectilePlanningStepper
    {
        public override int PlanProjectileSpawns(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiProjectileIntent> destination)
        {
            Assert.True(npcs.TryDespawn(source.Handle));
            NpcStateUpdate replacement = CreateNpcUpdate(99, 500f);
            Assert.True(npcs.TrySpawn(source.Handle.Slot, in replacement, out _));
            return base.PlanProjectileSpawns(in source, in proposed, destination);
        }
    }
}
