using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class MoonLordNetworkSyncTests
{
    [Fact]
    public void Source_moon_lord_state_handoffs_force_packet_23_but_ordinary_ticks_remain_cadenced()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());

        NpcSnapshot core = Create(VanillaNpcIds.MoonLordCore, new NpcAiState(0f, 0f, 0f, 0f));
        AssertForced(stepper, in core, core.Ai with { Ai0 = -1f });

        NpcSnapshot introEnd = core with { Ai = core.Ai with { Ai0 = -1f, Ai1 = 59f }, Simulation = core.Simulation with { LocalAi = core.Simulation.LocalAi with { Ai3 = 1f } } };
        AssertForced(stepper, in introEnd, introEnd.Ai with { Ai0 = 0f, Ai1 = 0f });

        NpcSnapshot exposed = core with { Simulation = core.Simulation with { LocalAi = core.Simulation.LocalAi with { Ai3 = 1f } } };
        AssertForced(stepper, in exposed, exposed.Ai with { Ai0 = 1f });

        NpcSnapshot hand = Create(VanillaNpcIds.MoonLordHand, new NpcAiState(0f, 10f, 0f, 0f));
        AssertForced(stepper, in hand, hand.Ai with { Ai0 = 1f });

        NpcSnapshot head = Create(VanillaNpcIds.MoonLordHead, new NpcAiState(0f, 0f, 0f, 0f));
        AssertForced(stepper, in head, head.Ai with { Ai0 = 3f, Ai1 = 1f });

        NpcSnapshot deathray = head with { Ai = head.Ai with { Ai0 = 1f, Ai1 = 1_004f } };
        AssertForced(stepper, in deathray, deathray.Ai with { Ai1 = 1_005f });

        NpcSnapshot ordinary = deathray with { Ai = deathray.Ai with { Ai1 = 1_003f } };
        AssertCadenced(stepper, in ordinary, ordinary.Ai with { Ai1 = 1_004f });
        NpcSnapshot retiredHand = hand with { Ai = hand.Ai with { Ai0 = -2f } };
        AssertCadenced(stepper, in retiredHand, retiredHand.Ai with { Ai1 = 11f });

        NpcSnapshot eye = Create(VanillaNpcIds.MoonLordFreeEye, new NpcAiState(0f, 53f, 0f, 0f));
        AssertForced(stepper, in eye, eye.Ai with { Ai0 = 1f, Ai1 = 54f });
        NpcSnapshot sphereRelease = eye with { Ai = eye.Ai with { Ai0 = 2f, Ai1 = 270f } };
        AssertForced(stepper, in sphereRelease, sphereRelease.Ai with { Ai1 = 271f });
        NpcSnapshot eyeDeathray = eye with { Ai = eye.Ai with { Ai0 = 4f, Ai1 = 816f } };
        AssertForced(stepper, in eyeDeathray, eyeDeathray.Ai with { Ai1 = 817f });
        NpcSnapshot ordinaryEye = sphereRelease with { Ai = sphereRelease.Ai with { Ai1 = 269f } };
        AssertCadenced(stepper, in ordinaryEye, ordinaryEye.Ai with { Ai1 = 270f });
    }

    private static void AssertForced(VanillaNpcTargetingAiStepper stepper, in NpcSnapshot npc, in NpcAiState ai)
    {
        NpcStateUpdate update = Update(in npc, in ai);
        Assert.True(stepper.RequiresForcedUpdate(in npc, in update));
    }

    private static void AssertCadenced(VanillaNpcTargetingAiStepper stepper, in NpcSnapshot npc, in NpcAiState ai)
    {
        NpcStateUpdate update = Update(in npc, in ai);
        Assert.False(stepper.RequiresForcedUpdate(in npc, in update));
    }

    private static NpcStateUpdate Update(in NpcSnapshot npc, in NpcAiState ai) =>
        new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, ai, npc.Simulation);

    private static NpcSnapshot Create(NpcTypeId type, NpcAiState ai) =>
        new(new NpcHandle(7, new NpcGeneration(1)), new NpcRevision(1), type.Value, checked((short)type.Value),
            100f, 200f, 1f, -2f, 0, ai, NpcSimulationState.Initial);

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
