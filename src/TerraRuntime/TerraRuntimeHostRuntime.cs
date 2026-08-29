using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;

namespace TerraRuntime;

internal sealed class TerraRuntimeHostRuntime : ITerraRuntimeHostRuntime
{
    public TerraRuntimeHostRuntime(
        TerraRuntimeHostRuntimeInfo info,
        IInterestManagementControl interestManagement,
        IPlayerStateSnapshotReader playerStates)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        InterestManagement = interestManagement ?? throw new ArgumentNullException(nameof(interestManagement));
        PlayerStates = playerStates ?? throw new ArgumentNullException(nameof(playerStates));
        if (playerStates is not RuntimePlayerStateSnapshotReader runtimePlayerStates)
        {
            throw new ArgumentException(
                "The production host runtime requires the authoritative runtime player snapshot reader.",
                nameof(playerStates));
        }

        NpcActors = new RuntimeNpcActorOperations(runtimePlayerStates.CommandIngress);
    }

    public TerraRuntimeHostRuntimeInfo Info { get; }
    public IInterestManagementControl InterestManagement { get; }
    public IPlayerStateSnapshotReader PlayerStates { get; }
    public INpcActorOperations NpcActors { get; }
}
