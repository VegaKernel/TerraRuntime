using TerraRuntime.Gameplay.Npcs;
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
        Assert.Equal(VanillaNpcDefinitionCatalog.NewNpcTimeLeft, servant.Simulation.TimeLeft);

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
    public void Expert_eye_uses_forty_four_tick_cadence_and_six_pixel_servant_speed()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate eye = CreateEye(ai3: 43f);
        Assert.True(store.TrySpawnVanilla(in eye, out _));

        var stepper = CreateEyeStepper(expertMode: true);
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(store.TryGetActive(1, out NpcSnapshot servant));
        float speed = MathF.Sqrt(
            servant.VelocityX * servant.VelocityX +
            servant.VelocityY * servant.VelocityY);
        Assert.Equal(6f, speed, 5);
    }

    [Fact]
    public void Committed_transition_applies_multiple_spawn_intents_in_source_order()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate sourceState = CreateOrdinary(VanillaNpcIds.BlueSlime.Value, 10f);
        Assert.True(store.TrySpawn(0, in sourceState, out NpcSnapshot source));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(new OrderedBatchPlanner(spawnCount: 2));

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(store.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(new NpcRevision(2), committed.Revision);
        Assert.True(store.TryGetActive(1, out NpcSnapshot first));
        Assert.True(store.TryGetActive(2, out NpcSnapshot second));
        Assert.Equal(90f, first.PositionX);
        Assert.Equal(80f, first.PositionY);
        Assert.Equal(190f, second.PositionX);
        Assert.Equal(180f, second.PositionY);
    }

    [Fact]
    public void Batch_spawn_is_best_effort_in_order_when_table_fills_mid_batch()
    {
        var store = new RuntimeNpcStore(capacity: 3);
        NpcStateUpdate sourceState = CreateOrdinary(VanillaNpcIds.BlueSlime.Value, 10f);
        Assert.True(store.TrySpawn(0, in sourceState, out _));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(new OrderedBatchPlanner(spawnCount: 3));

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(3, store.ActiveCount);
        Assert.True(store.TryGetActive(1, out NpcSnapshot first));
        Assert.True(store.TryGetActive(2, out NpcSnapshot second));
        Assert.Equal(90f, first.PositionX);
        Assert.Equal(190f, second.PositionX);
    }

    [Fact]
    public void Rejected_stale_source_transition_cannot_leak_spawn_batch()
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
    public void Invalid_planner_count_rejects_source_transition_before_commit()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate original = CreateOrdinary(VanillaNpcIds.BlueSlime.Value, 10f);
        Assert.True(store.TrySpawn(0, in original, out NpcSnapshot source));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(new InvalidCountPlanner());

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 0, 1), summary);
        Assert.True(store.TryGet(source.Handle, out NpcSnapshot current));
        Assert.Equal(new NpcRevision(1), current.Revision);
        Assert.Equal(1, store.ActiveCount);
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

    private static VanillaNpcTargetingAiStepper CreateEyeStepper(bool expertMode = false)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: expertMode);
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

    private sealed class OrderedBatchPlanner(int spawnCount) : INpcAiStateStepper, INpcAiSpawnIntentPlanner
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
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

        public int PlanNpcSpawns(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiSpawnIntent> destination)
        {
            Assert.InRange(spawnCount, 0, destination.Length);
            for (int index = 0; index < spawnCount; index++)
            {
                destination[index] = new NpcAiSpawnIntent(
                    VanillaNpcIds.ServantOfCthulhu,
                    BottomX: 100 + index * 100,
                    BottomY: 100 + index * 100,
                    VelocityX: 1f,
                    VelocityY: 0f,
                    Target: VanillaNpcDefinitionCatalog.DefaultTarget);
            }

            return spawnCount;
        }
    }

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

        public int PlanNpcSpawns(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiSpawnIntent> destination)
        {
            Assert.True(destination.Length >= 2);
            destination[0] = new NpcAiSpawnIntent(
                VanillaNpcIds.ServantOfCthulhu,
                BottomX: 100,
                BottomY: 100,
                VelocityX: 1f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget);
            destination[1] = destination[0] with { BottomX = 200 };
            return 2;
        }
    }

    private sealed class InvalidCountPlanner : INpcAiStateStepper, INpcAiSpawnIntentPlanner
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
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

        public int PlanNpcSpawns(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiSpawnIntent> destination) => destination.Length + 1;
    }
}
