using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ServerRuntimeState
{
    public void Apply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _runtime.Commands.Record();

        if (_runtime.ServerPlayers?.TryApply(command) == true)
            return;
        if (_runtime.WorldTileAuthority.TryApply(command))
            return;
        if (_runtime.Players.TryApply(command))
            return;
        if (_runtime.Projectiles.TryApply(command))
            return;
        if (_runtime.Npcs.TryApply(command))
            return;
        if (_runtime.WorldItems.TryApply(command))
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
