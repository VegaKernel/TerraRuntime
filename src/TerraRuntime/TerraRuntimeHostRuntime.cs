using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;

namespace TerraRuntime;

internal sealed class TerraRuntimeHostRuntime : ITerraRuntimeHostRuntime
{
    public TerraRuntimeHostRuntime(
        TerraRuntimeHostRuntimeInfo info,
        IInterestManagementControl interestManagement,
        IPlayerStateSnapshotReader playerStates,
        INpcActorOperations npcActors)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        InterestManagement = interestManagement ?? throw new ArgumentNullException(nameof(interestManagement));
        PlayerStates = playerStates ?? throw new ArgumentNullException(nameof(playerStates));
        NpcActors = npcActors ?? throw new ArgumentNullException(nameof(npcActors));
    }

    public TerraRuntimeHostRuntimeInfo Info { get; }
    public IInterestManagementControl InterestManagement { get; }
    public IPlayerStateSnapshotReader PlayerStates { get; }
    public INpcActorOperations NpcActors { get; }
}
