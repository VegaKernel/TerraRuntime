using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState
{
    public void Apply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AppliedCommands++;

        if (_serverPlayers?.TryApply(command) == true)
            return;
        if (_worldTileAuthority.TryApply(this, command))
            return;
        if (_players.TryApply(command))
            return;
        if (_projectiles.TryApply(command))
            return;
        if (_npcs.TryApply(command))
            return;
        if (_worldItems.TryApply(command))
            return;

        switch (command)
        {
            case WorkerResultCommand result:
                Volatile.Write(ref lastWorkerResult, result.Value);
                break;
            case SetInterestManagementRuntimeCommand interestManagement:
                interestManagement.Control.SetEnabled(interestManagement.Enabled);
                break;
            case PlayerStateSnapshotRuntimeCommand snapshot:
                CompletePlayerSnapshot(snapshot);
                break;
        }
    }
}
