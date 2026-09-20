using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class MoonLordFreeEyeTests
{
    [Fact]
    public void Hover_reacquires_the_closest_player_and_uses_source_above_player_steering()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            new NpcAiState(0f, 0f, 0f, 0f), new NpcAiState(.2f, .3f, .4f, 0f), 255);
        var random = new CountingRandom(1);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, 1500f, 820f, 0, true, false, false, false)
        {
            VelocityX = 3.25f,
            VelocityY = -1.75f
        }]);

        new RuntimeNpcAiStateExecutor(npcs).Tick(new EyeOnly(stepper));

        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(0f, next.Ai.Ai0);
        Assert.Equal(1f, next.Ai.Ai1);
        Assert.Equal(2.636120f, next.VelocityX, 5);
        Assert.Equal(-3.282218f, next.VelocityY, 5);
        Assert.Equal(-.012253493f, next.Simulation.LocalAi.Ai0, 5);
        Assert.Equal(.35f, next.Simulation.LocalAi.Ai1, 5);
        Assert.Equal(.52f, next.Simulation.LocalAi.Ai2, 5);
        Assert.True(next.Simulation.DontTakeDamage);
        Assert.Equal(1, random.Draws);
    }

    [Fact]
    public void Retired_eye_keeps_its_marker_until_the_attack_table_reaches_hover()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 0f, 0f,
            new NpcAiState(-2f, 52f, 0f, 0f), default, 255);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new CountingRandom(1));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500f, 820f, 0, true, false, false, false)]);

        new RuntimeNpcAiStateExecutor(npcs).Tick(new EyeOnly(stepper));

        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal(-2f, next.Ai.Ai0);
        Assert.Equal(53f, next.Ai.Ai1);
        Assert.Equal((ushort)0, next.Target);
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store, byte slot, NpcTypeId type, float x, float y,
        float vx, float vy, NpcAiState ai, NpcAiState local, ushort target)
    {
        var state = new NpcStateUpdate(type.Value, checked((short)type.Value), x, y, vx, vy, target, ai,
            NpcSimulationState.Initial with { LocalAi = local });
        Assert.True(store.TrySpawn(slot, in state, out NpcSnapshot npc));
        return npc;
    }

    private sealed class CountingRandom(int value) : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.Equal(0, inclusiveMin);
            Assert.Equal(420, exclusiveMax);
            Draws++;
            return value;
        }
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class EyeOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;

        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.TypeIdentity == VanillaNpcIds.MoonLordFreeEye)
                return inner.TryStepState(in npc, out next);
            next = default;
            return false;
        }
    }
}
