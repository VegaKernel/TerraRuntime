using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.HostContracts;

/// <summary>
/// Narrow runtime service surface attached after the authoritative game loop has started.
/// It exposes snapshots and controlled operations, never mutable implementation state.
/// </summary>
public interface ITerraRuntimeHostRuntime
{
    TerraRuntimeHostRuntimeInfo Info { get; }
    IInterestManagementControl InterestManagement { get; }
    IPlayerStateSnapshotReader PlayerStates { get; }
}
