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
    internal bool TryCapturePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)
        => _playerMembership.TryCapture(player, out snapshot);

    private bool TryCaptureRuntimePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (TryCapturePlayerSnapshot(player, out snapshot))
            return true;

        if (_serverPlayerStates is not null && _serverPlayerStates.TryGet(player, out snapshot))
            return true;

        snapshot = default;
        return false;
    }

    bool IRuntimePlayerSnapshotLookup.TryGetPlayer(
        PlayerHandle player,
        out PlayerStateSnapshot snapshot) =>
        TryCaptureRuntimePlayerSnapshot(player, out snapshot);

    bool IRuntimePlayerSlotSnapshotLookup.TryGetPlayer(
        PlayerSlotId slot,
        out PlayerStateSnapshot snapshot)
    {
        if (_playerMembership.TryGet(slot, out RuntimePlayerMember? player))
            return TryCaptureRuntimePlayerSnapshot(player.Connection.Player, out snapshot);

        if (_serverPlayerStates is not null)
            return _serverPlayerStates.TryGet(slot, out snapshot);

        snapshot = default;
        return false;
    }

    internal bool TryCapturePlayerInventoryItem(
        PlayerHandle player,
        int inventorySlot,
        out RuntimePlayerInventoryItem item)
    {
        if (!_playerMembership.TryGet(player, out RuntimePlayerMember? state))
        {
            item = default;
            return false;
        }

        return _playerInventory.TryGet(state.Connection, inventorySlot, out item);
    }

    internal bool TryCaptureNpcSnapshot(NpcHandle npc, out NpcSnapshot snapshot) =>
        _npcs.TryGet(npc, out snapshot);

    internal bool TryCaptureProjectileSnapshot(ProjectileHandle projectile, out ProjectileSnapshot snapshot) =>
        _projectiles.TryGet(projectile, out snapshot);

    internal bool TryCaptureWorldItemSnapshot(short slot, out WorldItemSnapshot snapshot) =>
        _worldItems.TryGetActive(slot, out snapshot);
}
