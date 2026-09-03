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
        => _players.TryCapture(player, out snapshot);

    private bool TryCaptureRuntimePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (TryCapturePlayerSnapshot(player, out snapshot))
            return true;

        if (_serverPlayers is not null && _serverPlayers.TryGet(player, out snapshot))
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
        if (_players.TryGet(slot, out RuntimePlayerMember? player))
            return TryCaptureRuntimePlayerSnapshot(player.Connection.Player, out snapshot);

        if (_serverPlayers is not null)
            return _serverPlayers.TryGet(slot, out snapshot);

        snapshot = default;
        return false;
    }

    internal bool TryCapturePlayerInventoryItem(
        PlayerHandle player,
        int inventorySlot,
        out RuntimePlayerInventoryItem item)
    {
        return _players.TryGetInventoryItem(player, inventorySlot, out item);
    }

    internal bool TryGetPlayerTalkNpc(PlayerHandle player, out short npcSlot) =>
        _players.TryGetTalkNpc(player, out npcSlot);

    internal bool TryCaptureNpcSnapshot(NpcHandle npc, out NpcSnapshot snapshot) =>
        _npcs.TryCapture(npc, out snapshot);

    internal int CopyCombatIntegrityDiagnostics(Span<CombatIntegrityDiagnostic> destination) =>
        _npcs.CopyCombatIntegrityDiagnostics(destination);

    internal bool TryCaptureProjectileSnapshot(ProjectileHandle projectile, out ProjectileSnapshot snapshot) =>
        _projectiles.TryCapture(projectile, out snapshot);

    internal bool TryCaptureWorldItemSnapshot(short slot, out WorldItemSnapshot snapshot) =>
        _worldItems.TryCapture(slot, out snapshot);

    private void CompletePlayerSnapshot(PlayerStateSnapshotRuntimeCommand command)
    {
        PlayerStateSnapshot? result = TryCaptureRuntimePlayerSnapshot(command.Player, out PlayerStateSnapshot snapshot)
            ? snapshot
            : null;
        command.Completion.TrySetResult(result);
    }
}
