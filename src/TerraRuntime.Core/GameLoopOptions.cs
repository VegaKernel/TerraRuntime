namespace TerraRuntime.Core;

public sealed record GameLoopOptions
{
    public const int DefaultTicksPerSecond = 60;
    private const double MaximumRepresentableCpuBudgetMilliseconds = long.MaxValue / 1_000_000d;

    public int TicksPerSecond { get; init; } = DefaultTicksPerSecond;

    public int CommandCapacity { get; init; } = 8192;

    public int MaxCommandIngressPerTick { get; init; } = 2048;

    public int MaxCommandsPerTick { get; init; } = 1024;

    public int MaxCommandsPerSourcePerTick { get; init; } = 128;

    /// <summary>
    /// Maximum queued authoritative commands retained for one external source such as a connection.
    /// Runtime/system work is exempt. Values above <see cref="CommandCapacity"/> are harmless because
    /// the global mailbox remains the tighter ceiling for such configurations.
    /// </summary>
    public int MaxPendingCommandsPerSource { get; init; } = 1024;

    /// <summary>
    /// Optional authoritative-thread CPU budget for command application in one tick.
    /// The hard operation budget remains active even when the platform CPU clock is unavailable.
    /// </summary>
    public double? MaxCommandCpuMillisecondsPerTick { get; init; }

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
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPendingCommandsPerSource, 1);

        if (MaxCommandCpuMillisecondsPerTick is double commandCpuBudget &&
            (!double.IsFinite(commandCpuBudget) ||
             commandCpuBudget <= 0d ||
             commandCpuBudget > MaximumRepresentableCpuBudgetMilliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxCommandCpuMillisecondsPerTick),
                "Command CPU budget must be a finite positive representable number of milliseconds.");
        }
    }
}
