using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryCaptureProjectileCombatSnapshot(PlayerHandle player, out VanillaPlayerCombatSnapshot snapshot)
    {
        snapshot = default;
        if (!membership.TryGet(player, out var member) ||
            member.ItemPhase?.DerivedCrit is not { } crit ||
            crit.Melee < 0 || crit.Ranged < 0 || crit.Magic < 0 ||
            !TryCaptureCombatSnapshot(member.Connection, out var equipment)) return false;

        // Player.Update derives these fields before ItemCheck. A later packet13/5 changes
        // the selected item/equipment without recomputing the last owned player phase.
        // Keep the equipment-only projection for direct melee, which adds its own item crit.
        snapshot = equipment with { MeleeCrit = crit.Melee, RangedCrit = crit.Ranged, MagicCrit = crit.Magic };
        return true;
    }
}
