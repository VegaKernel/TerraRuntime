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
    private void ApplyNpcSpawn(NpcSpawnRuntimeCommand command)
    {
        NpcStateUpdate state = command.State;
        if (_npcs.TrySpawn(command.Slot, in state, out NpcSnapshot snapshot))
        {
            AppliedNpcSpawns++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedNpcSpawns++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyNpcActorSpawn(NpcActorSpawnRuntimeCommand command)
    {
        NpcActorSpawnRequest request = command.Request;
        if (!request.IsValid)
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.InvalidRequest, default));
            return;
        }

        _npcArchetypes.CommitPending();
        if (!_npcArchetypes.Snapshot.TryGet(request.ArchetypeId, out _))
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.ArchetypeNotFound, default));
            return;
        }

        var spawn = new NpcArchetypeAllocateRequest(request.ArchetypeId, request.PositionX, request.PositionY);
        if (!_npcArchetypeSpawner.TrySpawnAllocated(in spawn, out NpcSnapshot snapshot))
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.NoAvailableSlot, default));
            return;
        }

        AppliedNpcSpawns++;
        command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.Spawned, snapshot.Handle));
    }

    private void ApplyNpcUpdate(NpcUpdateRuntimeCommand command)
    {
        NpcStateUpdate state = command.State;
        if (_npcs.TryUpdate(command.Npc, in state, out _))
        {
            AppliedNpcUpdates++;
            return;
        }

        RejectedNpcUpdates++;
    }

    private void ApplyNpcDespawn(NpcDespawnRuntimeCommand command)
    {
        if (_npcs.TryDespawn(command.Npc))
        {
            AppliedNpcDespawns++;
            command.Completion?.TrySetResult(true);
            return;
        }

        RejectedNpcDespawns++;
        command.Completion?.TrySetResult(false);
    }

    private void ApplyClientNpcDamage(ClientNpcDamageRuntimeCommand command)
    {
        TerrariaNpcDamageState damageState = command.State;
        if (!IsCurrentPlayerConnection(command.Connection))
        {
            RejectedClientNpcDamage++;
            return;
        }

        RuntimeNpcNetworkDamageResult result = _npcCombat.TryApply(command.Connection, in damageState);
        if (result == RuntimeNpcNetworkDamageResult.Rejected)
            RejectedClientNpcDamage++;
        else
            AppliedClientNpcDamage++;
    }

    private void ApplyClientNpcTalk(ClientNpcTalkRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            !TerrariaNpcTalkCodec.IsValidNpcSlot(command.State.NpcSlot))
        {
            return;
        }

        byte playerSlot = command.Connection.Player.Slot.Value;
        if (command.State.NpcSlot != TerrariaNpcTalkCodec.NoNpc)
            _townRescue?.TryRescueTalk(command.State.NpcSlot, out _);
        if (!_playerMembership.TrySetTalkNpc(command.Connection, command.State.NpcSlot))
            return;
        if (command.State.NpcSlot != TerrariaNpcTalkCodec.NoNpc &&
            _townCommerce is not null &&
            _playerMembership.TryGet(playerSlot, out RuntimePlayerMember? playerState))
        {
            var commercePlayer = new RuntimeTownCommercePlayer1458(
                playerState.PositionX,
                playerState.PositionY,
                playerState.HasHealth ? playerState.MaxLife : 100,
                playerState.HasMana ? playerState.MaxMana : 20,
                playerState.Team);
            if (_townCommerce.TryResolve(
                    command.Connection,
                    _playerInventory,
                    in commercePlayer,
                    command.State.NpcSlot,
                    _worldClock,
                    out RuntimeTownShopSession1458 session))
            {
                _playerMembership.TrySetTownShopSession(command.Connection, session);
            }
        }

        _npcReplication?.TryPublishNpcTalk(command.Connection, command.State.NpcSlot);
    }

    private void ApplyClientNpcCatch(ClientNpcCatchRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            !TerrariaNpcCatchCodec.IsValidNpcSlot(command.State.NpcSlot) ||
            !_playerMembership.TryGet(command.Connection, out RuntimePlayerMember? player) ||
            !_npcs.TryGetActive(checked((byte)command.State.NpcSlot), out NpcSnapshot npc) ||
            !NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcCatchCatalog1458.TryGetCatchItem(npcType, out ItemTypeId catchItem))
        {
            return;
        }

        if (VanillaNpcCatchCatalog1458.IsMysticFrog(npcType))
        {
            _mysticFrogCatch?.TryApply(npc.Handle, out _);
            return;
        }

        if (npc.Simulation.SpawnedFromStatue)
        {
            _npcs.TryDespawn(npc.Handle);
            return;
        }

        float playerCenterX = player.PositionX + VanillaBasePlayerWidth / 2f;
        float playerCenterY = player.PositionY + VanillaBasePlayerHeight / 2f;
        WorldItemDropStateUpdate drop = VanillaNpcCatchWorldItem1458.Create(
            playerCenterX,
            playerCenterY,
            catchItem,
            _worldItemSpawnRandom);
        if (!_worldItems.TryReserveDrop(in drop, out WorldItemDropReservation reservation))
            return;
        if (!_npcs.TryDespawn(npc.Handle))
        {
            _worldItems.TryReleaseDropReservation(in reservation);
            return;
        }
        if (!_worldItems.TryCommitReservedDrop(in reservation, out WorldItemSnapshot item))
            throw new InvalidOperationException("Reserved NPC catch item failed after authoritative NPC despawn.");

        var owner = new WorldItemOwnerStateUpdate(
            OwnerPlayerId: command.Connection.Player.Slot.Value,
            TimeToKeepReservation: VanillaNpcCatchWorldItem1458.ReservationTicks,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0,
            PositionX: item.PositionX,
            PositionY: item.PositionY);
        if (!_worldItems.TryApplyOwner(item.Handle.Slot, in owner, out _))
            throw new InvalidOperationException("Caught NPC item could not be reserved for the authenticated player.");
    }

    internal bool TryGetPlayerTalkNpc(PlayerHandle player, out short npcSlot)
        => _playerMembership.TryGetTalkNpc(player, out npcSlot);

    private void ApplyClientNpcHome(ClientNpcHomeRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            _townNpcs is null ||
            _housingValidator is null ||
            !command.State.TryGetStatus(out TerrariaNpcHomeStatus status))
        {
            return;
        }

        RuntimeTownNpcHomeCommit commit = default;
        bool applied = status switch
        {
            TerrariaNpcHomeStatus.Homeless => _townNpcs.TryKickOut(command.State.NpcSlot, out commit),
            TerrariaNpcHomeStatus.None => _townNpcs.TryAssignRoom(
                command.State.NpcSlot,
                command.State.HomeTileX,
                command.State.HomeTileY,
                _housingValidator,
                out commit,
                out _),
            // Status 2 is server-authored GetHouseholdStatus state, not a client room-move request.
            TerrariaNpcHomeStatus.HasRoom => false,
            _ => false
        };

        if (applied)
            _npcReplication?.TryPublishTownHome(in commit);
    }
}
