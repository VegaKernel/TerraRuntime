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
    IPlayerAdministrativeOperations PlayerAdministration => throw new NotSupportedException("Player administration is not exposed by this host runtime implementation.");
    INpcActorOperations NpcActors { get; }
    INpcShopOperations NpcShops { get; }
    IServerPlayerOperations ServerPlayers { get; }
}
