using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcBehaviorPerformanceTests
{
    private const int WarmupIterations = 65_536;
    private const int MeasuredIterations = 65_536;

    [Fact]
    public void Zero_extension_and_one_decorator_dispatch_remain_allocation_light_after_warmup()
    {
        var vanilla = new IncrementingStepper(1f);
        var emptyRegistry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        var direct = new RuntimeNpcBehaviorStateStepper(vanilla, emptyRegistry);

        var decoratedRegistry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        Assert.Equal(
            GameplayBehaviorRegistrationResult.Registered,
            decoratedRegistry.TryRegister(
                new GameplayExtensionId("performance:pre"),
                new NpcTypeId(1),
                GameplayBehaviorStage.Pre,
                order: 0,
                new IncrementingStepper(2f),
                out IGameplayBehaviorRegistrationLease? lease));
        using IGameplayBehaviorRegistrationLease registration = Assert.IsAssignableFrom<IGameplayBehaviorRegistrationLease>(lease);
        decoratedRegistry.CommitPending();
        var decorated = new RuntimeNpcBehaviorStateStepper(vanilla, decoratedRegistry);
        NpcSnapshot npc = CreateSnapshot();

        Run(direct, in npc, WarmupIterations);
        Run(decorated, in npc, WarmupIterations);

        long directAllocated = MeasureMinimumAllocation(direct, in npc, out float directChecksum);
        long decoratedAllocated = MeasureMinimumAllocation(decorated, in npc, out float decoratedChecksum);

        Assert.Equal(MeasuredIterations, directChecksum);
        Assert.Equal(MeasuredIterations * 3f, decoratedChecksum);
        Assert.True(directAllocated <= 256, $"Zero-extension NPC dispatch allocated {directAllocated} bytes.");
        Assert.True(decoratedAllocated <= 256, $"One-decorator steady-state NPC dispatch allocated {decoratedAllocated} bytes.");
    }

    private static long MeasureMinimumAllocation(
        RuntimeNpcBehaviorStateStepper stepper,
        in NpcSnapshot npc,
        out float checksum)
    {
        long minimum = long.MaxValue;
        checksum = 0f;
        for (int sample = 0; sample < 4; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            checksum = Run(stepper, in npc, MeasuredIterations);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            minimum = Math.Min(minimum, allocated);
        }

        return minimum;
    }

    private static float Run(RuntimeNpcBehaviorStateStepper stepper, in NpcSnapshot npc, int iterations)
    {
        float checksum = 0f;
        for (int index = 0; index < iterations; index++)
        {
            if (!stepper.TryStepState(in npc, out NpcStateUpdate next))
                throw new InvalidOperationException("Performance fixture produced no NPC update.");

            checksum += next.PositionX;
        }

        return checksum;
    }

    private static NpcSnapshot CreateSnapshot() =>
        new(
            new NpcHandle(0, new NpcGeneration(1)),
            new NpcRevision(1),
            Type: 1,
            NetId: 1,
            PositionX: 0f,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

    private sealed class IncrementingStepper(float deltaX) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX + deltaX,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }
    }
}
