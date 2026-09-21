using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaMoonEventMourningWoodAiTests
{
    [Fact]
    public void Type_315_defaults_coverage_and_ai_family_match_source()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(315), out VanillaNpcDefinition definition));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(315), out VanillaNpcAiCoverage coverage));

        Assert.Equal(new NpcAiStyleId(26), definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.MoonEventUnicorn, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.UnicornGround, definition.PhysicsFamily);
        Assert.Equal(74, definition.BaseWidth); Assert.Equal(70, definition.BaseHeight);
        Assert.Equal(130, definition.Damage); Assert.Equal(40, definition.Defense); Assert.Equal(5000, definition.LifeMax);
        Assert.Equal(0f, definition.KnockBackResist);
        Assert.True(coverage.Has(VanillaNpcAiCapability.UnicornTraversalSlice));
        Assert.True(coverage.Has(VanillaNpcAiCapability.MoonEventProjectileSlice));

        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(new NpcTypeId(315), 200, 300, 0f, 0f, 3), out _));
    }

    [Fact]
    public void Ai26_advances_mourning_wood_local_timer_and_uses_six_pixel_charge_limit()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper();
        NpcSnapshot npc = CreateNpc(localTimer: 479f);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(480f, next.Simulation.LocalAi.Ai0);
        Assert.Equal(.07f, next.VelocityX, 5);
        Assert.Equal((ushort)3, next.Target);
    }

    [Fact]
    public void Timer_boundary_spawns_server_projectile_after_committed_transition_with_pre_motion_facts()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(1, Update(localTimer: 480f, target: 3, spawnDifficulty: 2f), out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore();
        var random = new SequenceRandom(0d, .5d, .25d);
        VanillaNpcTargetingAiStepper ai = CreateStepper(random);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new MourningWoodOnly(ai)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Simulation.LocalAi.Ai0);
        Assert.Equal(1, projectiles.ActiveCount);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot projectile));
        Assert.Equal(VanillaProjectileIds.MourningWoodFireball, projectile.Type);
        Assert.Equal(144f, projectile.PositionX, 5); Assert.Equal(222f, projectile.PositionY, 5);
        Assert.Equal(2f, projectile.VelocityX, 5); Assert.Equal(-1.5f, projectile.VelocityY, 5);
        Assert.Equal((short)30, projectile.Damage); Assert.Equal(new ProjectileAiState(3f, 0f, 0f), projectile.Ai);
        Assert.True(projectiles.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(360, lifecycle.TimeLeft);
        Assert.True(projectiles.TryGetServerNpcSource(projectile.Handle, out NpcHandle provenance));
        Assert.Equal(source.Handle, provenance); Assert.Equal(3, random.Draws);
    }

    [Fact]
    public void Rejected_timer_transition_does_not_consume_rng_or_create_a_projectile()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(1, Update(localTimer: 480f, target: 3, spawnDifficulty: 1f), out _));
        var projectiles = new RuntimeProjectileStore();
        var random = new SequenceRandom(0d, .5d, .25d);
        VanillaNpcTargetingAiStepper ai = CreateStepper(random);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new ReplacingMourningWood(ai, npcs)).Rejected);
        Assert.Equal(0, projectiles.ActiveCount); Assert.Equal(0, random.Draws);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(IVanillaNpcRandom? random = null)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableZombieMotion(100d); stepper.SetMoonEventState(true);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, 300f, 115f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot CreateNpc(float localTimer) => new(
        new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 315, 315,
        100f, 200f, 0f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget, default,
        NpcSimulationState.Initial with
        {
            DirectionX = 1, DirectionY = 1, SpriteDirection = 1, OldPositionX = 99f,
            Life = 5000, LifeMax = 5000, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            LocalAi = new NpcAiState(localTimer, 0f, 0f, 0f)
        });

    private static NpcStateUpdate Update(float localTimer, ushort target, float spawnDifficulty) => new(
        315, 315, 100f, 200f, 2f, 0f, target, default,
        NpcSimulationState.Initial with
        {
            DirectionX = 1, DirectionY = 1, SpriteDirection = 1, OldPositionX = 99f,
            Life = 5000, LifeMax = 5000, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            SpawnDifficulty = spawnDifficulty, LocalAi = new NpcAiState(localTimer, 0f, 0f, 0f)
        });

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private class MourningWoodOnly(VanillaNpcTargetingAiStepper ai) : INpcAiStateStepper, INpcAiStatePostCommitEffect
    {
        public virtual bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.TypeIdentity == VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi26MourningWood)
                return ai.TryStepState(in npc, out next);
            next = default;
            return false;
        }

        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            ai.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private sealed class ReplacingMourningWood(VanillaNpcTargetingAiStepper ai, RuntimeNpcStore store) : MourningWoodOnly(ai)
    {
        public override bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (!base.TryStepState(in npc, out next)) return false;
            var superseding = next with { PositionX = next.PositionX + 1f };
            Assert.True(store.TryUpdate(npc.Handle, in superseding, out _));
            return true;
        }
    }

    private sealed class SequenceRandom(params double[] samples) : IVanillaNpcRandom
    {
        private int index;
        public int Draws => index;
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
        public double NextDouble() => samples[index++];
    }
}
