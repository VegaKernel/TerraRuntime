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
        if (_objectPlacementProcessor?.TryApply(this, command) == true)
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
            case ProjectileSpawnRuntimeCommand spawn:
                ApplyProjectileSpawn(spawn);
                break;
            case ProjectileUpdateRuntimeCommand update:
                ApplyProjectileUpdate(update);
                break;
            case ProjectileDespawnRuntimeCommand despawn:
                ApplyProjectileDespawn(despawn);
                break;
            case ClientProjectileUpdateRuntimeCommand update:
                ApplyClientProjectileUpdate(update);
                break;
            case ClientProjectileDestroyRuntimeCommand destroy:
                ApplyClientProjectileDestroy(destroy);
                break;
            case ClientNpcDamageRuntimeCommand npcDamage:
                ApplyClientNpcDamage(npcDamage);
                break;
            case ClientTileManipulationRuntimeCommand tile:
                ApplyClientTileManipulation(tile);
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
            case PlayerAppearanceRuntimeCommand appearance:
                ApplyPlayerAppearance(appearance);
                break;
            case PlayerEquipmentRuntimeCommand equipment:
                ApplyPlayerEquipment(equipment);
                break;
            case PlayerHealthRuntimeCommand health:
                ApplyPlayerHealth(health);
                break;
            case PlayerManaRuntimeCommand mana:
                ApplyPlayerMana(mana);
                break;
            case PlayerSpawnRuntimeCommand spawn:
                ApplyPlayerSpawn(spawn);
                break;
            case PlayerMovementRuntimeCommand movement:
                ApplyPlayerMovement(movement);
                break;
            case PlayerDisconnectRuntimeCommand disconnect:
                ApplyPlayerDisconnect(disconnect);
                break;
            case PlayerStateSnapshotRuntimeCommand snapshot:
                CompletePlayerSnapshot(snapshot);
                break;
            case PlayerTransferDetachRuntimeCommand detach:
                ApplyPlayerTransferDetach(detach);
                break;
            case PlayerTransferAttachRuntimeCommand attach:
                ApplyPlayerTransferAttach(attach);
                break;
        }
    }
}
