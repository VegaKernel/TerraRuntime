using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime;

internal sealed class TerraRuntimeHostRuntime : ITerraRuntimeHostRuntime
{
    public TerraRuntimeHostRuntime(
        TerraRuntimeHostRuntimeInfo info,
        IInterestManagementControl interestManagement,
        IPlayerStateSnapshotReader playerStates,
        IPlayerAdministrativeOperations playerAdministration,
        RuntimeNpcShopCatalogRegistry npcShops,
        RuntimeNpcArchetypeRegistry npcArchetypes)
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

        PlayerAdministration = playerAdministration ?? throw new ArgumentNullException(nameof(playerAdministration));
        NpcActors = new RuntimeNpcActorOperations(
            runtimePlayerStates.CommandIngress,
            npcArchetypes ?? throw new ArgumentNullException(nameof(npcArchetypes)));
        NpcShops = new RuntimeNpcShopOperations(npcShops ?? throw new ArgumentNullException(nameof(npcShops)));
        ServerPlayers = new RuntimeServerPlayerOperations(runtimePlayerStates.CommandIngress);
    }

    public TerraRuntimeHostRuntimeInfo Info { get; }
    public IInterestManagementControl InterestManagement { get; }
    public IPlayerStateSnapshotReader PlayerStates { get; }
    public IPlayerAdministrativeOperations PlayerAdministration { get; }
    public INpcActorOperations NpcActors { get; }
    public INpcShopOperations NpcShops { get; }
    public IServerPlayerOperations ServerPlayers { get; }
}
