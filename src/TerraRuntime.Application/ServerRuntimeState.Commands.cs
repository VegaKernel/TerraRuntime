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

        if (_serverPlayerCommands?.TryApply(command) == true)
            return;
        if (_npcActorCommands.TryApply(command))
            return;
        if (_worldTileAuthority.TryApply(this, command))
            return;
        if (_players.TryApply(command))
            return;
        if (_projectiles.TryApply(command))
            return;
        if (command is NpcActorSpawnRuntimeCommand actorSpawn)
        {
            ApplyNpcActorSpawn(actorSpawn);
            return;
        }

        switch (command)
        {
            case WorkerResultCommand result:
                Volatile.Write(ref lastWorkerResult, result.Value);
                break;
            case SetInterestManagementRuntimeCommand interestManagement:
                interestManagement.Control.SetEnabled(interestManagement.Enabled);
                break;
            case NpcSpawnRuntimeCommand spawn:
                ApplyNpcSpawn(spawn);
                break;
            case NpcUpdateRuntimeCommand update:
                ApplyNpcUpdate(update);
                break;
            case NpcDespawnRuntimeCommand despawn:
                ApplyNpcDespawn(despawn);
                break;
            case ClientNpcDamageRuntimeCommand npcDamage:
                ApplyClientNpcDamage(npcDamage);
                break;
            case ClientNpcHomeRuntimeCommand home:
                ApplyClientNpcHome(home);
                break;
            case ClientNpcTalkRuntimeCommand talk:
                ApplyClientNpcTalk(talk);
                break;
            case ClientNpcCatchRuntimeCommand npcCatch:
                ApplyClientNpcCatch(npcCatch);
                break;
            case WorldItemAllocateRuntimeCommand allocate:
                ApplyWorldItemAllocate(allocate);
                break;
            case WorldItemDropRuntimeCommand drop:
                ApplyWorldItemDrop(drop);
                break;
            case WorldItemRemoveRuntimeCommand remove:
                ApplyWorldItemRemove(remove);
                break;
            case WorldItemOwnerRuntimeCommand owner:
                ApplyWorldItemOwner(owner);
                break;
            case PlayerStateSnapshotRuntimeCommand snapshot:
                CompletePlayerSnapshot(snapshot);
                break;
        }
    }
}
