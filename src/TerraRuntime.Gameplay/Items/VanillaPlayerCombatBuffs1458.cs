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
        if (active.Length > 44 || !float.IsFinite(before.MinionDamage) || before.MinionDamage <= 0f) return false;
        if (before.Defense < 0 || before.MeleeCrit < 0 || before.RangedCrit < 0 || before.MagicCrit < 0) return false;
        foreach (var type in active)
        {
            // Source Player.UpdateBuffs 1.4.5.8 preserves indexed order and duplicate slots.
            // Long guards are imported-record safety; the source has no Endurance clamp.
            if (type.Value == 5)
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
            float.IsFinite(after.RangedDamage) && float.IsFinite(after.MagicDamage) && float.IsFinite(after.MinionDamage);
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
