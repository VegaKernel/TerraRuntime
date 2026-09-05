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
    internal bool TryCapturePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)
        => _runtime.Players.TryCapture(player, out snapshot);

    private bool TryCaptureRuntimePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot) =>
        _runtime.PlayerSnapshots.TryGetPlayer(player, out snapshot);

    internal bool TryCapturePlayerInventoryItem(
        PlayerHandle player,
        int inventorySlot,
        out RuntimePlayerInventoryItem item)
    {
        return _runtime.Players.TryGetInventoryItem(player, inventorySlot, out item);
    }

    internal bool TryGetPlayerTalkNpc(PlayerHandle player, out short npcSlot) =>
        _runtime.Players.TryGetTalkNpc(player, out npcSlot);

    internal bool TryCaptureNpcSnapshot(NpcHandle npc, out NpcSnapshot snapshot) =>
        _runtime.Npcs.TryCapture(npc, out snapshot);

    internal int CopyCombatIntegrityDiagnostics(Span<CombatIntegrityDiagnostic> destination) =>
        _runtime.Npcs.CopyCombatIntegrityDiagnostics(destination);

    internal bool TryCaptureProjectileSnapshot(ProjectileHandle projectile, out ProjectileSnapshot snapshot) =>
        _runtime.Projectiles.TryCapture(projectile, out snapshot);

    internal bool TryCaptureWorldItemSnapshot(short slot, out WorldItemSnapshot snapshot) =>
        _runtime.WorldItems.TryCapture(slot, out snapshot);

    private void CompletePlayerSnapshot(PlayerStateSnapshotRuntimeCommand command)
    {
        PlayerStateSnapshot? result = TryCaptureRuntimePlayerSnapshot(command.Player, out PlayerStateSnapshot snapshot)
            ? snapshot
            : null;
        command.Completion.TrySetResult(result);
    }
}
