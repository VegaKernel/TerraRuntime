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

    [Fact]
    public void Projectile_mutation_applies_only_after_source_commit_and_requires_exact_npc_generation()
    {
        var npcs = new RuntimeNpcStore(capacity: 2);
        var projectiles = new RuntimeProjectileStore(capacity: 6);
        NpcStateUpdate initial = CreateNpcUpdate(type: 1, positionX: 10f);
        Assert.True(npcs.TrySpawn(0, in initial, out NpcSnapshot source));

        var sphereIntent = new NpcAiProjectileIntent(
            VanillaProjectileIds.PhantasmalSphere,
            20f,
            30f,
            2f,
            3f,
            40,
            1f)
        {
            InitialAi = new ProjectileAiState(45f, source.Handle.Slot, 0f)
        };
        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(
            projectiles, source.Handle, in sphereIntent, out ProjectileSnapshot ownedSphere));

        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(
            projectiles, in sphereIntent, out ProjectileSnapshot unprovenancedSphere));

        var staleSource = new NpcHandle(source.Handle.Slot, new NpcGeneration(source.Handle.Generation.Value + 1));
        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(
            projectiles, staleSource, in sphereIntent, out ProjectileSnapshot staleSphere));

        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        NpcAiStateTickSummary summary = executor.Tick(new ProjectileMutationPlanningStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(projectiles.TryGet(ownedSphere.Handle, out ProjectileSnapshot released));
        Assert.Equal(-1f, released.Ai.Ai0);
        Assert.Equal(9f, released.VelocityX);
        Assert.Equal(-4f, released.VelocityY);

        Assert.True(projectiles.TryGet(unprovenancedSphere.Handle, out ProjectileSnapshot untouched));
        Assert.Equal(45f, untouched.Ai.Ai0);
        Assert.True(projectiles.TryGet(staleSphere.Handle, out ProjectileSnapshot staleUntouched));
        Assert.Equal(45f, staleUntouched.Ai.Ai0);
    }

    [Fact]
    public void Rejected_source_commit_cannot_mutate_existing_projectile()
    {
        var npcs = new RuntimeNpcStore(capacity: 2);
        var projectiles = new RuntimeProjectileStore(capacity: 3);
        NpcStateUpdate initial = CreateNpcUpdate(type: 1, positionX: 10f);
        Assert.True(npcs.TrySpawn(0, in initial, out NpcSnapshot source));
        var sphereIntent = new NpcAiProjectileIntent(
            VanillaProjectileIds.PhantasmalSphere, 0f, 0f, 1f, 0f, 40, 1f)
        {
            InitialAi = new ProjectileAiState(30f, source.Handle.Slot, 0f)
        };
        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(
            projectiles, source.Handle, in sphereIntent, out ProjectileSnapshot sphere));

        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        NpcAiStateTickSummary summary = executor.Tick(new ReplacingProjectileMutationPlanningStepper(npcs));

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 0, 1), summary);
        Assert.True(projectiles.TryGet(sphere.Handle, out ProjectileSnapshot untouched));
        Assert.Equal(30f, untouched.Ai.Ai0);
        Assert.Equal(1f, untouched.VelocityX);
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

    private class ProjectileMutationPlanningStepper : INpcAiStateStepper, INpcAiProjectileMutationIntentPlanner
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

        public virtual int PlanProjectileMutations(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiProjectileMutationIntent> destination)
        {
            destination[0] = new NpcAiProjectileMutationIntent(
                VanillaProjectileIds.PhantasmalSphere,
                9f,
                -4f,
                -1f);
            return 1;
        }
    }

    private sealed class ReplacingProjectileMutationPlanningStepper(RuntimeNpcStore npcs) : ProjectileMutationPlanningStepper
    {
        public override int PlanProjectileMutations(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiProjectileMutationIntent> destination)
        {
            Assert.True(npcs.TryDespawn(source.Handle));
            NpcStateUpdate replacement = CreateNpcUpdate(99, 500f);
            Assert.True(npcs.TrySpawn(source.Handle.Slot, in replacement, out _));
            return base.PlanProjectileMutations(in source, in proposed, destination);
        }
    }
}
