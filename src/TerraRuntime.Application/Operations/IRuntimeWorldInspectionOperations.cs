using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application.Operations;

/// <summary>
/// Process-owned detached inspection boundary for live world runtimes. Callers select by stable runtime ID;
/// sandbox regeneration may rotate the session ID without invalidating the operator selection.
/// </summary>
internal interface IRuntimeWorldInspectionOperations
{
    ReadOnlyMemory<RuntimeWorldInspectionTarget> CaptureTargets();

    bool TryCaptureRuntime(WorldRuntimeId runtimeId, out WorldRuntimeSnapshot snapshot);

    bool TryCapturePlayers(WorldRuntimeId runtimeId, out RuntimePlayersSnapshot snapshot);

    bool TryCaptureNpcs(WorldRuntimeId runtimeId, out RuntimeNpcsSnapshot snapshot);

    bool TryCaptureProjectiles(WorldRuntimeId runtimeId, out RuntimeProjectilesSnapshot snapshot);

    bool TryCaptureWorldItems(WorldRuntimeId runtimeId, out RuntimeWorldItemsSnapshot snapshot);
}

internal readonly record struct RuntimeWorldInspectionTarget(
    WorldRuntimeId RuntimeId,
    string DisplayName,
    bool IsPrimary,
    WorldRuntimeLifecycle Lifecycle,
    WorldSessionId SessionId,
    int TargetTicksPerSecond,
    double ObservedTicksPerSecond);
