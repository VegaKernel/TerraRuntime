using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
namespace TerraRuntime.Tests;
public sealed class RuntimeNpcStinkyAiAdmission1458Tests
{
    [Fact]
    public void Real_world_tick_runs_owned_non_town_visual_random_phase_and_ai_through_expiry()
    {
        var npcs = new RuntimeNpcStore(); var source = new NpcStateUpdate(3, 3, 100, 100, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100 }); Assert.True(npcs.TrySpawn(0, in source, out var npc));
        var stepper = new CountingStep(); var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: stepper);
        var runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        Assert.True(runtime.Npcs.NpcBuffStatus.TryApply(npc.Handle, 1));
        Assert.True(runtime.Npcs.NpcBuffStatus.TryGetStinky(npc.Handle, out bool immediate)); Assert.False(immediate);
        state.Tick(); Assert.Equal(1, stepper.Calls); Assert.True(npcs.TryGet(npc.Handle, out var first)); Assert.Equal(101f, first.PositionX);
        Assert.True(runtime.Npcs.NpcBuffStatus.TryGetWireDuration(npc.Handle, out int duration)); Assert.Equal(-1, duration);
        state.Tick(); Assert.Equal(2, stepper.Calls); Assert.True(npcs.TryGet(npc.Handle, out var advanced)); Assert.Equal(102f, advanced.PositionX);
    }
    [Fact]
    public void Admission_preserves_wrapper_capabilities_and_new_generation_known_clear()
    {
        var npcs = new RuntimeNpcStore(); var source = new NpcStateUpdate(3, 3, 100, 100, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100 }); Assert.True(npcs.TrySpawn(0, in source, out var npc));
        var status = new RuntimeNpcBuffStatus1458(npcs); var inner = new CountingStep();
        var wrapped = new RuntimeNpcBuffAiStepper1458(inner, status, new SystemVanillaNpcRandom(1458));
        Assert.Same(inner, NpcAiStateStepperComposition.FindCapability<CountingStep>(wrapped));
        Assert.True(status.TryApply(npc.Handle, 180)); status.BeginWorldTick(); Assert.True(wrapped.TryStepState(in npc, out _));
        Assert.True(npcs.TryDespawn(npc.Handle)); Assert.True(npcs.TrySpawn(0, in source, out var replaced));
        Assert.True(wrapped.TryStepState(in replaced, out _)); Assert.Equal(2, inner.Calls);
    }
    [Fact]
    public void Stale_generation_cannot_run_visual_offer_or_partial_ai()
    {
        var npcs = new RuntimeNpcStore();
        var input = new NpcStateUpdate(3, 3, 100, 100, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100 });
        Assert.True(npcs.TrySpawn(0, in input, out var stale));
        var status = new RuntimeNpcBuffStatus1458(npcs);
        Assert.True(status.TryApply(stale.Handle, 180)); status.BeginWorldTick();
        Assert.True(npcs.TryDespawn(stale.Handle)); Assert.True(npcs.TrySpawn(0, in input, out _));
        var inner = new CountingStep(); var random = new VanillaUnifiedRandom1458(1458);
        var expected = random.Clone();
        var wrapper = new RuntimeNpcBuffAiStepper1458(inner, status, new SystemVanillaNpcRandom(random));
        Assert.False(wrapper.TryStepState(in stale, out _));
        Assert.Equal(0, inner.Calls); Assert.True(random.HasSameState(expected));
    }

    private sealed class CountingStep : INpcAiStateStepper
    {
        internal int Calls;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { Calls++; next = new(npc.Type, npc.NetId, npc.PositionX + 1, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation); return true; }
    }
}
