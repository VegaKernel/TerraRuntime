using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaBrainOfCthulhuBehaviorTests
{
    [Fact]
    public void Definitions_match_1458_defaults_and_brain_spawns_invulnerable()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BrainOfCthulhu, out VanillaNpcDefinition brain));
        Assert.Equal(54, brain.AiStyle.Value);
        Assert.Equal((160, 110, 30, 14, 1250), (brain.BaseWidth, brain.BaseHeight, brain.Damage, brain.Defense, brain.LifeMax));
        Assert.Equal(0.45f, brain.KnockBackResist, 5);

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BrainCreeper, out VanillaNpcDefinition creeper));
        Assert.Equal(55, creeper.AiStyle.Value);
        Assert.Equal((30, 30, 20, 10, 100), (creeper.BaseWidth, creeper.BaseHeight, creeper.Damage, creeper.Defense, creeper.LifeMax));
        Assert.Equal(0.8f, creeper.KnockBackResist, 5);

        var store = new RuntimeNpcStore(capacity: 64);
        NpcStateUpdate update = BrainUpdate();
        Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot spawned));
        Assert.True(spawned.Simulation.DontTakeDamage);
        Assert.Equal(1250, spawned.Simulation.Life);
        Assert.Equal(1250, spawned.Simulation.LifeMax);
    }

    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 40)]
    public void First_authoritative_tick_spawns_source_count_creepers(bool goodWorld, int expected)
    {
        var store = new RuntimeNpcStore(capacity: 64);
        NpcStateUpdate update = BrainUpdate();
        Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot brain));
        var stepper = CreateStepper(goodWorld, new ZeroRandom());
        stepper.SetCandidates([Target()]);

        var executor = new RuntimeNpcAiStateExecutor(store);
        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(1, summary.Applied);
        Assert.True(store.TryGet(brain.Handle, out NpcSnapshot committed));
        Assert.Equal(1f, committed.Simulation.LocalAi.Ai0);
        Assert.True(committed.Simulation.DontTakeDamage);

        var active = new NpcSnapshot[64];
        int count = store.CopyActive(active);
        int creepers = 0;
        foreach (NpcSnapshot npc in active.AsSpan(0, count))
        {
            if (npc.TypeIdentity == VanillaNpcIds.BrainCreeper)
                creepers++;
        }
        Assert.Equal(expected, creepers);
    }

    [Fact]
    public void Brain_enters_phase_two_after_last_creeper_and_then_becomes_damageable()
    {
        var store = new RuntimeNpcStore(capacity: 64);
        NpcStateUpdate update = BrainUpdate();
        Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot brain));
        var stepper = CreateStepper(goodWorld: false, new ZeroRandom());
        stepper.SetCandidates([Target()]);
        var executor = new RuntimeNpcAiStateExecutor(store);

        executor.Tick(stepper);
        var active = new NpcSnapshot[64];
        int count = store.CopyActive(active);
        foreach (NpcSnapshot npc in active.AsSpan(0, count))
        {
            if (npc.TypeIdentity == VanillaNpcIds.BrainCreeper)
                Assert.True(store.TryDespawn(npc.Handle));
        }

        executor.Tick(stepper);
        Assert.True(store.TryGet(brain.Handle, out NpcSnapshot transition));
        Assert.Equal(-1f, transition.Ai.Ai0);
        Assert.True(transition.Simulation.DontTakeDamage);

        executor.Tick(stepper);
        Assert.True(store.TryGet(brain.Handle, out NpcSnapshot phaseTwo));
        Assert.Equal(-1f, phaseTwo.Ai.Ai0);
        Assert.False(phaseTwo.Simulation.DontTakeDamage);
    }

    [Fact]
    public void Creeper_without_live_brain_expires()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        var update = new NpcStateUpdate(
            VanillaNpcIds.BrainCreeper.Value,
            checked((short)VanillaNpcIds.BrainCreeper.Value),
            100f, 100f, 0f, 0f,
            VanillaNpcDefinitionCatalog.DefaultTarget,
            default,
            NpcSimulationState.Initial);
        Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot creeper));

        var stepper = CreateStepper(goodWorld: false, new ZeroRandom());
        var executor = new RuntimeNpcAiStateExecutor(store);
        executor.Tick(stepper);

        Assert.True(store.TryGet(creeper.Handle, out NpcSnapshot expired));
        Assert.Equal(0, expired.Simulation.Life);
        Assert.Equal(0, expired.Simulation.TimeLeft);
    }

    [Fact]
    public void Expert_creeper_charge_uses_eight_pixel_launch()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate brainUpdate = BrainUpdate(positionX: 100f, positionY: 100f);
        Assert.True(store.TrySpawnVanilla(in brainUpdate, out _));
        var creeperUpdate = new NpcStateUpdate(
            VanillaNpcIds.BrainCreeper.Value,
            checked((short)VanillaNpcIds.BrainCreeper.Value),
            150f, 130f, 1f, 1f,
            VanillaNpcDefinitionCatalog.DefaultTarget,
            default,
            NpcSimulationState.Initial);
        Assert.True(store.TrySpawnVanilla(in creeperUpdate, out NpcSnapshot creeper));

        var stepper = CreateStepper(goodWorld: false, new ZeroRandom(), expertMode: true);
        stepper.SetCandidates([Target(centerX: 300f, centerY: 150f)]);
        var executor = new RuntimeNpcAiStateExecutor(store);
        executor.Tick(stepper);

        Assert.True(store.TryGet(creeper.Handle, out NpcSnapshot charged));
        Assert.Equal(1f, charged.Ai.Ai0);
        float speed = MathF.Sqrt(charged.VelocityX * charged.VelocityX + charged.VelocityY * charged.VelocityY);
        Assert.Equal(8f, speed, 4);
        Assert.Equal((ushort)0, charged.Target);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool goodWorld, IVanillaNpcRandom random, bool expertMode = false)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetBrainOfCthulhuEnvironment(new EmptyEnvironment());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, goodWorld: goodWorld, expertMode: expertMode);
        return stepper;
    }

    private static NpcStateUpdate BrainUpdate(float positionX = 100f, float positionY = 100f) =>
        new(
            VanillaNpcIds.BrainOfCthulhu.Value,
            checked((short)VanillaNpcIds.BrainOfCthulhu.Value),
            positionX,
            positionY,
            0f,
            0f,
            VanillaNpcDefinitionCatalog.DefaultTarget,
            default,
            NpcSimulationState.Initial);

    private static VanillaNpcTargetCandidate Target(float centerX = 180f, float centerY = 180f) =>
        new(0, centerX, centerY, 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class EmptyEnvironment : IVanillaBrainOfCthulhuEnvironment
    {
        public bool IsSolidTile(int tileX, int tileY) => false;

        public bool CanHit(
            float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;
    }

    private sealed class ZeroRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }
}
