using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal enum IncomingHumanCombatPolicy1458 : byte
{
    EquipmentComponent,
    PhaseOwned
}

internal readonly record struct IncomingPlayerCombatCapture1458(
    RuntimePlayerMember? Member,
    PlayerStateSnapshot Player,
    RuntimePlayerItemPhase1458? ItemPhase,
    ulong InventorySerial,
    bool GodMode,
    VanillaPlayerCombatSnapshot Combat);

internal sealed partial class PlayerAuthority
{
    private readonly IncomingHumanCombatPolicy1458 incomingHumanCombatPolicy;

    internal bool TryCaptureIncomingCombat(PlayerHandle player, out IncomingPlayerCombatCapture1458 capture)
    {
        capture = default;
        if (!membership.TryGet(player, out var member))
        {
            // Server-owned actors retain their separate equipment/component policy.
            if (!TryCaptureCombatTarget(player.Slot.Value, out var server) || server.Player != player ||
                !TryCaptureCombatSnapshot(player, out var serverCombat)) return false;
            capture = new(null, server, null, inventory.Serial, server.GodMode, serverCombat);
            return true;
        }

        RuntimePlayerItemPhase1458? phase = member.ItemPhase;
        if ((member.Revision == ulong.MaxValue || member.ProjectileUseInputRevision == ulong.MaxValue ||
            inventory.Serial == ulong.MaxValue) && !member.GodMode) return false;
        VanillaPlayerCombatSnapshot combat;
        if (member.GodMode)
            combat = default; // Hurt avoids damage before using derived mitigation. This does not mint a phase.
        else if (incomingHumanCombatPolicy == IncomingHumanCombatPolicy1458.PhaseOwned)
        {
            if (phase?.DerivedCombat is not { } derived || !IsValidDerivedCombat(in derived)) return false;
            combat = derived;
        }
        else if (!TryCaptureCombatSnapshot(player, out combat))
            return false;

        capture = new(member, member.CaptureSnapshot(), phase, inventory.Serial, member.GodMode, combat);
        return true;
    }

    internal bool IsCurrentIncomingCombat(in IncomingPlayerCombatCapture1458 capture)
    {
        if (capture.Member is null)
            return TryCaptureCombatTarget(capture.Player.Player.Slot.Value, out var server) && server == capture.Player;
        return membership.TryGet(capture.Player.Player, out var member) && ReferenceEquals(member, capture.Member) &&
            member.CaptureSnapshot() == capture.Player && member.ItemPhase == capture.ItemPhase && member.GodMode == capture.GodMode &&
            inventory.Serial == capture.InventorySerial;
    }
}
