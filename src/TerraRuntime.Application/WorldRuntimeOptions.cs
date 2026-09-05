using TerraRuntime.Core;

namespace TerraRuntime.Application;

public sealed record WorldRuntimeOptions
{
    public int MaxPlayers { get; init; } = ServerHostOptions.DefaultMaxPlayers;

    public int TargetTicksPerSecond { get; init; } = GameLoopOptions.DefaultTicksPerSecond;

    public bool CaptureOperationsTelemetry { get; init; }

    internal GameLoopOptions CreateLoopOptions()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPlayers, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxPlayers, byte.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(TargetTicksPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TargetTicksPerSecond, 1000);
        return new GameLoopOptions { TicksPerSecond = TargetTicksPerSecond };
    }
}
