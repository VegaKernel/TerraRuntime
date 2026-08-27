namespace TerraRuntime.Core;

public sealed record GameLoopOptions
{
    public const int DefaultTicksPerSecond = 60;

    public int TicksPerSecond { get; init; } = DefaultTicksPerSecond;

    public int CommandCapacity { get; init; } = 8192;

    public int MaxCommandsPerTick { get; init; } = 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(TicksPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TicksPerSecond, 1000);
        ArgumentOutOfRangeException.ThrowIfLessThan(CommandCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxCommandsPerTick, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxCommandsPerTick, CommandCapacity);
    }
}
