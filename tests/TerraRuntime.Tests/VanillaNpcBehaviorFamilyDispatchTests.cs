using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcBehaviorFamilyDispatchTests
{
    [Fact]
    public void Disabled_slime_family_falls_back_to_bounded_inner_stepper()
    {
        var inner = new RecordingStepper();
        var stepper = new VanillaNpcTargetingAiStepper(inner);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BlueSlime.Value);

        Assert.True(stepper.TryStepState(in npc, out _));

        Assert.Equal(1, inner.Calls);
        Assert.Equal(VanillaNpcIds.BlueSlime.Value, inner.Last.Type);
    }

    [Fact]
    public void Unknown_catalog_type_does_not_inherit_a_behavior_from_numeric_similarity()
    {
        var inner = new RecordingStepper();
        var stepper = new VanillaNpcTargetingAiStepper(inner);
        NpcSnapshot npc = CreateNpc(type: 4);

        Assert.True(stepper.TryStepState(in npc, out _));

        Assert.Equal(1, inner.Calls);
        Assert.Equal(4, inner.Last.Type);
    }

    [Fact]
    public void Flying_eye_family_owns_target_refresh_before_delegating_behavior_core()
    {
        var inner = new RecordingStepper();
        var stepper = new VanillaNpcTargetingAiStepper(inner);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 40f,
                CenterY: 40f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.DemonEye.Value);

        Assert.True(stepper.TryStepState(in npc, out _));

        Assert.Equal(1, inner.Calls);
        Assert.Equal((ushort)7, inner.Last.Target);
        Assert.Equal(-1, inner.Last.Simulation.DirectionX);
        Assert.Equal(-1, inner.Last.Simulation.DirectionY);
    }

    private static NpcSnapshot CreateNpc(int type) =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: type,
            NetId: checked((short)type),
            PositionX: 100f,
            PositionY: 80f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });

    private sealed class RecordingStepper : INpcAiStateStepper
    {
        public int Calls { get; private set; }

        public NpcSnapshot Last { get; private set; }

        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            Calls++;
            Last = npc;
            next = default;
            return true;
        }
    }
}
