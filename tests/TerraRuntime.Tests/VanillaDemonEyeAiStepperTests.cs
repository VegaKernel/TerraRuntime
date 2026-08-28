using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaDemonEyeAiStepperTests
{
    [Fact]
    public void Executor_applies_verified_type2_motion_and_no_gravity_state()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            DirectionX = -1,
            DirectionY = -1
        };
        var state = new NpcStateUpdate(
            Type: 2,
            NetId: 2,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 1f,
            VelocityY: 1f,
            Target: 0,
            Ai: default,
            Simulation: simulation);
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(new VanillaDemonEyeAiStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(0.95f, updated.VelocityX, precision: 5);
        Assert.Equal(0.99f, updated.VelocityY, precision: 5);
        Assert.True(updated.Simulation.NoGravity);
        Assert.Equal((ulong)2, updated.Revision.Value);
    }

    [Fact]
    public void Non_type2_npc_is_left_for_its_own_ai_style()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        var state = new NpcStateUpdate(
            Type: 1,
            NetId: 1,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 1f,
            VelocityY: 1f,
            Target: 0,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(new VanillaDemonEyeAiStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 0, 0, 0), summary);
        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot unchanged));
        Assert.Equal((ulong)1, unchanged.Revision.Value);
        Assert.False(unchanged.Simulation.NoGravity);
    }

    [Fact]
    public void Collision_and_wet_context_flow_through_authoritative_state()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            DirectionX = 1,
            DirectionY = -1,
            OldVelocityX = 1f,
            OldVelocityY = -0.5f,
            CollideX = true,
            CollideY = true,
            Wet = true
        };
        var state = new NpcStateUpdate(
            Type: 2,
            NetId: 2,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 8f,
            VelocityY: 8f,
            Target: 0,
            Ai: default,
            Simulation: simulation);
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(new VanillaDemonEyeAiStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(-1.95f, updated.VelocityX, precision: 5);
        Assert.Equal(0.4405f, updated.VelocityY, precision: 4);
        Assert.True(updated.Simulation.Wet);
        Assert.True(updated.Simulation.NoGravity);
    }
}
