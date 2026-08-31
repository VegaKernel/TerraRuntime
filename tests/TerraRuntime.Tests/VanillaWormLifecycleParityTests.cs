using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaWormLifecycleParityTests
{
    [Fact]
    public void Eater_body_with_two_active_foreign_style_links_splits_into_head_instead_of_dying()
    {
        var stepper = CreateStepper();
        NpcSnapshot predecessor = CreateSnapshot(1, VanillaNpcIds.BlueSlime, default);
        NpcSnapshot successor = CreateSnapshot(2, VanillaNpcIds.Zombie, default);
        stepper.SetNpcPeers([predecessor, successor]);
        NpcSnapshot body = CreateSnapshot(
            3,
            VanillaNpcIds.EaterOfWorldsBody,
            new NpcAiState(Ai0: 2f, Ai1: 1f, Ai2: -1f, Ai3: 3f));

        Assert.True(stepper.TryStepState(in body, out NpcStateUpdate next));

        Assert.Equal(VanillaNpcIds.EaterOfWorldsHead.Value, next.Type);
        Assert.Equal(body.Simulation.Life, next.Simulation.Life);
        Assert.Equal(2f, next.Ai.Ai0);
        Assert.Equal(0f, next.Ai.Ai1);
    }

    [Fact]
    public void Eater_head_uses_active_only_successor_death_gate()
    {
        var stepper = CreateStepper();
        NpcSnapshot foreignSuccessor = CreateSnapshot(2, VanillaNpcIds.BlueSlime, default);
        stepper.SetNpcPeers([foreignSuccessor]);
        NpcSnapshot head = CreateSnapshot(
            3,
            VanillaNpcIds.EaterOfWorldsHead,
            new NpcAiState(Ai0: 2f, Ai1: 0f, Ai2: -1f, Ai3: 3f));

        Assert.True(stepper.TryStepState(in head, out NpcStateUpdate next));

        Assert.Equal(VanillaNpcIds.EaterOfWorldsHead.Value, next.Type);
        Assert.Equal(head.Simulation.Life, next.Simulation.Life);
        Assert.NotEqual(0, next.Simulation.TimeLeft);
    }

    [Fact]
    public void Eater_tail_uses_active_only_predecessor_death_gate()
    {
        var stepper = CreateStepper();
        NpcSnapshot foreignPredecessor = CreateSnapshot(1, VanillaNpcIds.BlueSlime, default);
        stepper.SetNpcPeers([foreignPredecessor]);
        NpcSnapshot tail = CreateSnapshot(
            3,
            VanillaNpcIds.EaterOfWorldsTail,
            new NpcAiState(Ai0: 0f, Ai1: 1f, Ai2: 0f, Ai3: 3f));

        Assert.True(stepper.TryStepState(in tail, out NpcStateUpdate next));

        Assert.Equal(VanillaNpcIds.EaterOfWorldsTail.Value, next.Type);
        Assert.Equal(tail.Simulation.Life, next.Simulation.Life);
        Assert.NotEqual(0, next.Simulation.TimeLeft);
    }

    [Fact]
    public void Eater_body_with_both_structural_links_missing_is_terminal()
    {
        var stepper = CreateStepper();
        stepper.SetNpcPeers([]);
        NpcSnapshot body = CreateSnapshot(
            3,
            VanillaNpcIds.EaterOfWorldsBody,
            new NpcAiState(Ai0: 2f, Ai1: 1f, Ai2: -1f, Ai3: 3f));

        Assert.True(stepper.TryStepState(in body, out NpcStateUpdate next));

        Assert.Equal(0, next.Simulation.Life);
        Assert.Equal(0, next.Simulation.TimeLeft);
    }

    [Fact]
    public void Eater_body_with_foreign_predecessor_and_worm_successor_splits_into_head()
    {
        var stepper = CreateStepper();
        NpcSnapshot foreignPredecessor = CreateSnapshot(1, VanillaNpcIds.BlueSlime, default);
        NpcSnapshot wormSuccessor = CreateSnapshot(2, VanillaNpcIds.EaterOfWorldsBody, default);
        stepper.SetNpcPeers([foreignPredecessor, wormSuccessor]);
        NpcSnapshot body = CreateSnapshot(
            3,
            VanillaNpcIds.EaterOfWorldsBody,
            new NpcAiState(Ai0: 2f, Ai1: 1f, Ai2: -1f, Ai3: 3f));

        Assert.True(stepper.TryStepState(in body, out NpcStateUpdate next));

        Assert.Equal(VanillaNpcIds.EaterOfWorldsHead.Value, next.Type);
        Assert.Equal(2f, next.Ai.Ai0);
    }

    [Fact]
    public void Eater_head_spawn_propagates_root_slot_through_ai3()
    {
        var stepper = CreateStepper();
        NpcSnapshot head = CreateSnapshot(
            7,
            VanillaNpcIds.EaterOfWorldsHead,
            new NpcAiState(Ai0: 0f, Ai1: 0f, Ai2: 0f, Ai3: 0f));
        NpcStateUpdate proposed = ToUpdate(in head);
        var intents = new NpcAiSpawnIntent[4];

        int count = stepper.PlanNpcSpawns(in head, in proposed, intents);

        Assert.Equal(1, count);
        Assert.Equal(7f, intents[0].InitialAi.Ai3);
        Assert.Equal(7f, intents[0].InitialAi.Ai1);
    }

    [Fact]
    public void Eater_body_spawn_preserves_existing_root_slot_through_ai3()
    {
        var stepper = CreateStepper();
        NpcSnapshot body = CreateSnapshot(
            8,
            VanillaNpcIds.EaterOfWorldsBody,
            new NpcAiState(Ai0: 0f, Ai1: 7f, Ai2: 20f, Ai3: 7f));
        NpcStateUpdate proposed = ToUpdate(in body);
        var intents = new NpcAiSpawnIntent[4];

        int count = stepper.PlanNpcSpawns(in body, in proposed, intents);

        Assert.Equal(1, count);
        Assert.Equal(7f, intents[0].InitialAi.Ai3);
        Assert.Equal(8f, intents[0].InitialAi.Ai1);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper() =>
        new(new PassthroughStepper());

    private static NpcSnapshot CreateSnapshot(byte slot, NpcTypeId type, NpcAiState ai) =>
        new(
            new NpcHandle(slot, new NpcGeneration(1)),
            new NpcRevision(1),
            type.Value,
            checked((short)type.Value),
            PositionX: 100f + slot * 20f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: ai,
            Simulation: NpcSimulationState.Initial with
            {
                Life = 100,
                LifeMax = 100,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                NoGravity = true,
                NoTileCollide = true
            });

    private static NpcStateUpdate ToUpdate(in NpcSnapshot npc) =>
        new(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            npc.VelocityX,
            npc.VelocityY,
            npc.Target,
            npc.Ai,
            npc.Simulation);

    private sealed class PassthroughStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = ToUpdate(in npc);
            return true;
        }
    }
}
