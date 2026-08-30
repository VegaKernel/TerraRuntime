using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcAiSpawnIntentTests
{
    [Fact]
    public void Eye_servant_is_spawned_only_after_source_state_commit()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate eye = CreateEye(ai3: 109f);
        Assert.True(store.TrySpawnVanilla(in eye, out NpcSnapshot source));

        var stepper = CreateEyeStepper();
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(2, store.ActiveCount);
        Assert.True(store.TryGet(source.Handle, out NpcSnapshot committedEye));
        Assert.Equal(0f, committedEye.Ai.Ai3);
        Assert.True(store.TryGetActive(1, out NpcSnapshot servant));
        Assert.Equal(VanillaNpcIds.ServantOfCthulhu.Value, servant.Type);
        Assert.Equal((short)VanillaNpcIds.ServantOfCthulhu.Value, servant.NetId);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, servant.Target);
        Assert.Equal(8, servant.Simulation.Life);
        Assert.Equal(8, servant.Simulation.LifeMax);
        Assert.True(servant.Simulation.NoGravity);
        Assert.True(servant.Simulation.NoTileCollide);
        Assert.Equal(VanillaNpcSpawnFacts.NewNpcTimeLeft, servant.Simulation.TimeLeft);

        float deltaX = 250f - 150f;
        float deltaY = 300f - 155f;
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        float expectedVelocityX = deltaX * (5f / distance);
        float expectedVelocityY = deltaY * (5f / distance);
        int expectedBottomX = (int)(150f + expectedVelocityX * 10f);
        int expectedBottomY = (int)(155f + expectedVelocityY * 10f);
        Assert.Equal(expectedBottomX - 10f, servant.PositionX, 5);
        Assert.Equal(expectedBottomY - 20f, servant.PositionY, 5);
        Assert.Equal(expectedVelocityX, servant.VelocityX, 5);
        Assert.Equal(expectedVelocityY, servant.VelocityY, 5);
    }

    [Fact]
    public void Rejected_stale_source_transition_cannot_leak_spawn_intent()
    {
        var store = new RuntimeNpcStore(capacity: 3);
        NpcStateUpdate original = CreateOrdinary(type: 1, positionX: 10f);
        Assert.True(store.TrySpawn(0, in original, out _));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(new ReplacingSpawnPlanner(store));

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 0, 1), summary);
        Assert.Equal(1, store.ActiveCount);
        Assert.True(store.TryGetActive(0, out NpcSnapshot replacement));
        Assert.Equal(99, replacement.Type);
        Assert.False(store.TryGetActive(1, out _));
        Assert.False(store.TryGetActive(2, out _));
    }

    [Fact]
    public void Full_npc_table_drops_spawn_but_keeps_committed_boss_cadence()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcStateUpdate eye = CreateEye(ai3: 109f);
        Assert.True(store.TrySpawnVanilla(in eye, out NpcSnapshot source));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(CreateEyeStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(1, store.ActiveCount);
        Assert.True(store.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Ai.Ai3);
    }

    private static VanillaNpcTargetingAiStepper CreateEyeStepper()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 250f,
                CenterY: 300f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        return stepper;
    }

    private static NpcStateUpdate CreateEye(float ai3) =>
        new(
            Type: VanillaNpcIds.EyeOfCthulhu.Value,
            NetId: checked((short)VanillaNpcIds.EyeOfCthulhu.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 7,
            Ai: new NpcAiState(0f, 0f, 42f, ai3),
            Simulation: NpcSimulationState.Initial with
            {
                Life = 2800,
                LifeMax = 2800,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                NoGravity = true,
                NoTileCollide = true
            });

    private static NpcStateUpdate CreateOrdinary(int type, float positionX) =>
        new(
            Type: type,
            NetId: checked((short)type),
            PositionX: positionX,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

    private sealed class ReplacingSpawnPlanner(RuntimeNpcStore store) : INpcAiStateStepper, INpcAiSpawnIntentPlanner
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            Assert.True(store.TryDespawn(npc.Handle));
            NpcStateUpdate replacement = CreateOrdinary(type: 99, positionX: 500f);
            Assert.True(store.TrySpawn(npc.Handle.Slot, in replacement, out _));

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

        public bool TryPlanNpcSpawn(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            out NpcAiSpawnIntent intent)
        {
            intent = new NpcAiSpawnIntent(
                VanillaNpcIds.ServantOfCthulhu,
                BottomX: 100,
                BottomY: 100,
                VelocityX: 1f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget);
            return true;
        }
    }
}
