namespace TerraRuntime.Core;

public sealed record GameLoopOptions
{
    public const int DefaultTicksPerSecond = 60;

    public int TicksPerSecond { get; init; } = DefaultTicksPerSecond;

    public int CommandCapacity { get; init; } = 8192;

    public int MaxCommandIngressPerTick { get; init; } = 2048;

    public int MaxCommandsPerTick { get; init; } = 1024;

    public int MaxCommandsPerSourcePerTick { get; init; } = 128;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(TicksPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TicksPerSecond, 1000);
        ArgumentOutOfRangeException.ThrowIfLessThan(CommandCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxCommandIngressPerTick, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxCommandIngressPerTick, CommandCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxCommandsPerTick, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxCommandsPerTick, CommandCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxCommandsPerSourcePerTick, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxCommandsPerSourcePerTick, MaxCommandsPerTick);
    }
}
