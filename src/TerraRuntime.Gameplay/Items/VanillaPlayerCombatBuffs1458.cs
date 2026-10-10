using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Bounded indexed Archery/Wrath writes from original Player.UpdateBuffs 1.4.5.8.
/// The caller owns source reset/early branches, ordered active slots and expiration.</summary>
public static class VanillaPlayerCombatBuffs1458
{
    public static bool TryApply(in VanillaPlayerCombatSnapshot before, ReadOnlySpan<BuffTypeId> active,
        out VanillaPlayerCombatSnapshot after)
    {
        after = before;
        if (active.Length > 44 || !float.IsFinite(before.MinionDamage) || before.MinionDamage <= 0f) return false;
        foreach (var type in active)
        {
            if (type.Value == 16) after = after with { Archery = true, ArrowDamage = after.ArrowDamage * 1.1f };
            else if (type.Value == 117) after = after with { MeleeDamage = after.MeleeDamage + 0.1f,
                RangedDamage = after.RangedDamage + 0.1f, MagicDamage = after.MagicDamage + 0.1f,
                MinionDamage = after.MinionDamage + 0.1f };
        }
        return float.IsFinite(after.ArrowDamage) && float.IsFinite(after.MeleeDamage) &&
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
