using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Bounded indexed combat-buff writes from original Player.UpdateBuffs 1.4.5.8.
/// The caller owns source reset/early branches, ordered active slots and expiration.</summary>
public static class VanillaPlayerCombatBuffs1458
{
    public static bool TryApply(in VanillaPlayerCombatSnapshot before, ReadOnlySpan<BuffTypeId> active,
        out VanillaPlayerCombatSnapshot after)
    {
        after = before;
        if ((before.BuffImmunity & ~VanillaPlayerBuffImmunity1458.All) != 0 || active.Length > 44 || !float.IsFinite(before.MinionDamage) || before.MinionDamage <= 0f ||
            !float.IsFinite(before.MeleeAttackSpeed) || !float.IsFinite(before.MinionKnockback) || !float.IsFinite(before.MoveSpeed) ||
            !float.IsFinite(before.PickSpeed)) return false;
        if (before.Defense < 0 || before.MeleeCrit < 0 || before.RangedCrit < 0 || before.MagicCrit < 0) return false;
        foreach (var type in active)
        {
            // Source Player.UpdateBuffs 1.4.5.8 preserves indexed order and duplicate slots.
            // Long guards are imported-record safety; the source has no Endurance clamp.
            if (type == VanillaBuffIds.WellFed || type == VanillaBuffIds.WellFed2 || type == VanillaBuffIds.WellFed3)
            {
                int tier = type == VanillaBuffIds.WellFed ? 1 : type == VanillaBuffIds.WellFed2 ? 2 : 3;
                int defenseAndCrit = tier + 1;
                float damageAndAttackSpeed = tier == 1 ? .05f : tier == 2 ? .075f : .1f;
                float knockback = tier == 1 ? .5f : tier == 2 ? .75f : 1f;
                float movement = tier == 1 ? .2f : tier == 2 ? .3f : .4f;
                float pick = tier == 1 ? .05f : tier == 2 ? .1f : .15f;
                if ((long)after.Defense + defenseAndCrit > int.MaxValue ||
                    (long)after.MeleeCrit + defenseAndCrit > int.MaxValue ||
                    (long)after.RangedCrit + defenseAndCrit > int.MaxValue ||
                    (long)after.MagicCrit + defenseAndCrit > int.MaxValue) return false;
                after = after with
                {
                    WellFed = true,
                    Defense = after.Defense + defenseAndCrit,
                    MeleeCrit = after.MeleeCrit + defenseAndCrit,
                    MeleeDamage = after.MeleeDamage + damageAndAttackSpeed,
                    MeleeAttackSpeed = after.MeleeAttackSpeed + damageAndAttackSpeed,
                    MagicCrit = after.MagicCrit + defenseAndCrit,
                    MagicDamage = after.MagicDamage + damageAndAttackSpeed,
                    RangedCrit = after.RangedCrit + defenseAndCrit,
                    RangedDamage = after.RangedDamage + damageAndAttackSpeed,
                    MinionDamage = after.MinionDamage + damageAndAttackSpeed,
                    MinionKnockback = after.MinionKnockback + knockback,
                    MoveSpeed = after.MoveSpeed + movement,
                    PickSpeed = after.PickSpeed - pick
                };
            }
            else if (type.Value == 5)
            {
                if ((long)after.Defense + 8 > int.MaxValue) return false;
                after = after with { Defense = after.Defense + 8 };
            }
            else if (type.Value == 114) after = after with { Endurance = after.Endurance + 0.1f };
            else if (type.Value is 115 or 321)
            {
                if ((long)after.MeleeCrit + 10 > int.MaxValue || (long)after.RangedCrit + 10 > int.MaxValue ||
                    (long)after.MagicCrit + 10 > int.MaxValue) return false;
                after = after with { MeleeCrit = after.MeleeCrit + 10, RangedCrit = after.RangedCrit + 10,
                    MagicCrit = after.MagicCrit + 10,
                    MinionDamage = type.Value == 321 ? after.MinionDamage + 0.1f : after.MinionDamage };
            }
            else if (type.Value == 16) after = after with { Archery = true, ArrowDamage = after.ArrowDamage * 1.1f };
            else if (type.Value == 117) after = after with { MeleeDamage = after.MeleeDamage + 0.1f,
                RangedDamage = after.RangedDamage + 0.1f, MagicDamage = after.MagicDamage + 0.1f,
                MinionDamage = after.MinionDamage + 0.1f };
        }
        return float.IsFinite(after.Endurance) && float.IsFinite(after.ArrowDamage) && float.IsFinite(after.MeleeDamage) &&
            float.IsFinite(after.RangedDamage) && float.IsFinite(after.MagicDamage) && float.IsFinite(after.MinionDamage) &&
            float.IsFinite(after.MeleeAttackSpeed) && float.IsFinite(after.MinionKnockback) &&
            float.IsFinite(after.MoveSpeed) && float.IsFinite(after.PickSpeed);
    }

    /// <summary>Source retained manaSickReduction store precedes the late magic multiply;
    /// CLR4 x86 retains the subtraction intermediate wider than the CoreCLR Single path.</summary>
    public static bool TryResolveMagicDamage(float damage, int sickness, float heat, bool windows, out float result)
    {
        result = 0f;
        if (!float.IsFinite(damage) || damage < 0f || sickness < 0 || !float.IsFinite(heat) || heat < 0f) return false;
        if (sickness > 0)
        {
            float reduction = windows ? (float)(0.25 * ((double)(float)sickness / 300.0)) : 0.25f * ((float)sickness / 300f);
            damage = windows ? (float)(damage * (1.0 - reduction)) : damage * (1f - reduction);
        }
        result = damage * heat;
        return float.IsFinite(result) && result >= 0f;
    }
}
