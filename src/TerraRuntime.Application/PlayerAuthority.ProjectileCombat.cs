using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryCaptureProjectileCombatSnapshot(PlayerHandle player, out VanillaPlayerCombatSnapshot snapshot)
    {
        snapshot = default;
        if (!membership.TryGet(player, out var member) ||
            member.ItemPhase?.DerivedCombat is not { } combat || !IsValidDerivedCombat(in combat)) return false;

        // Player.Update derives the whole represented projection before ItemCheck. Later
        // selection/equipment reports cannot splice current gear into a prior phase's fields.
        // Keep the equipment-only projection for direct melee, which adds its own item crit.
        snapshot = combat;
        return true;
    }

    private static bool IsValidDerivedCombat(in VanillaPlayerCombatSnapshot combat) =>
        combat.Defense >= 0 && combat.MeleeCrit >= 0 && combat.RangedCrit >= 0 && combat.MagicCrit >= 0 &&
        combat.ArmorPenetration >= 0 && combat.MeleeArmorPenetration >= 0 && combat.LavaProtectionTicks >= 0 &&
        float.IsFinite(combat.Endurance) && combat.Endurance >= 0f &&
        IsNonnegativeFinite(combat.MeleeDamage) && IsNonnegativeFinite(combat.RangedDamage) &&
        IsNonnegativeFinite(combat.MinionDamage) && combat.MinionDamage > 0f &&
        IsNonnegativeFinite(combat.MagicDamage) && float.IsFinite(combat.RangedMultDamage) && combat.RangedMultDamage > 0f &&
        IsNonnegativeFinite(combat.ArrowDamage) && IsNonnegativeFinite(combat.ArrowDamageAdditiveStack) &&
        IsNonnegativeFinite(combat.BulletDamage) && IsNonnegativeFinite(combat.MeleeAttackSpeed) &&
        IsNonnegativeFinite(combat.BowDamageMultiplier) && IsNonnegativeFinite(combat.GunDamageMultiplier) &&
        IsNonnegativeFinite(combat.MeleeAnimationMultiplier) &&
        (long)combat.ArmorPenetration + combat.MeleeArmorPenetration <= int.MaxValue;

    private static bool IsNonnegativeFinite(float value) => float.IsFinite(value) && value >= 0f;
}
