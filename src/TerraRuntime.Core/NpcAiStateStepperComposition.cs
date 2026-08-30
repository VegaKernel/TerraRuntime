namespace TerraRuntime.Core;

/// <summary>
/// Explicit composition marker for allocation-stable NPC AI decorators. It lets runtime orchestration discover
/// optional capabilities owned by a nested stepper without hard-coding every decorator type or bypassing the
/// authoritative state pipeline.
/// </summary>
public interface INpcAiStateStepperWrapper
{
    INpcAiStateStepper InnerStepper { get; }
}

public static class NpcAiStateStepperComposition
{
    private const int MaximumWrapperDepth = 32;

    public static TCapability? FindCapability<TCapability>(INpcAiStateStepper stepper)
        where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(stepper);

        INpcAiStateStepper current = stepper;
        for (int depth = 0; depth < MaximumWrapperDepth; depth++)
        {
            if (current is TCapability capability)
                return capability;

            if (current is not INpcAiStateStepperWrapper wrapper ||
                ReferenceEquals(wrapper.InnerStepper, current))
            {
                return null;
            }

            current = wrapper.InnerStepper;
        }

        return null;
    }
}
