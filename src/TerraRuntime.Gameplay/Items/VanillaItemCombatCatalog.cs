using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Source-backed direct-melee combat facts imported independently from TerrariaServer 1.4.5.8 Item.SetDefaults.
/// This catalog is intentionally opt-in: absence means that authoritative combat must not invent weapon stats.
/// Player-wide equipment/buff modifiers remain separate inputs to the combat calculator.
/// </summary>
public readonly record struct VanillaDirectMeleeCombatDefinition(
    ItemTypeId Type,
    int BaseDamage,
    float BaseKnockBack,
    int BaseCrit,
    int UseTimeTicks,
    int AnimationTicks,
    float ImpossibleCenterDistancePixels);

/// <summary>Prefix multipliers consumed by the verified direct-melee authoritative slice.</summary>
public readonly record struct VanillaCombatPrefixModifiers(
    float DamageMultiplier,
    float KnockBackMultiplier,
    float SpeedMultiplier,
    int CritBonus,
    int ArmorPenetration)
{
    public static VanillaCombatPrefixModifiers Identity => new(1f, 1f, 1f, 0, 0);
}

public static class VanillaItemCombatCatalog
{
    // Player.ResetEffects starts the ordinary melee critical chance at 4 in the pinned server source.
    public const int VanillaBaseMeleeCrit = 4;

    private static readonly VanillaDirectMeleeCombatDefinition Muramasa = new(
        VanillaItemIds.Muramasa,
        BaseDamage: 24,
        BaseKnockBack: 3f,
        BaseCrit: VanillaBaseMeleeCrit,
        UseTimeTicks: 18,
        AnimationTicks: 18,
        // This is deliberately a generous impossible-distance ceiling rather than an exact swing rectangle.
        // Exact per-animation item rectangles belong to the later full melee geometry slice.
        ImpossibleCenterDistancePixels: 192f);

    private static readonly VanillaDirectMeleeCombatDefinition CopperPickaxe = new(
        VanillaItemIds.CopperPickaxe,
        BaseDamage: 4,
        BaseKnockBack: 2f,
        BaseCrit: VanillaBaseMeleeCrit,
        UseTimeTicks: 15,
        AnimationTicks: 23,
        ImpossibleCenterDistancePixels: 192f);

    public static bool TryGetDirectMelee(ItemTypeId type, out VanillaDirectMeleeCombatDefinition definition)
    {
        if (type == VanillaItemIds.Muramasa)
        {
            definition = Muramasa;
            return true;
        }
        if (type == VanillaItemIds.CopperPickaxe)
        {
            definition = CopperPickaxe;
            return true;
        }

        definition = default;
        return false;
    }

    public static bool TryGetPrefixModifiers(PrefixId prefix, out VanillaCombatPrefixModifiers modifiers)
    {
        if (prefix == VanillaPrefixIds.None)
        {
            modifiers = VanillaCombatPrefixModifiers.Identity;
            return true;
        }

        modifiers = prefix.Value switch
        {
            38 => new(1f, 1.15f, 1f, 0, 0),       // Forceful
            39 => new(0.70f, 0.80f, 1f, 0, 0),   // Broken
            40 => new(0.85f, 1f, 1f, 0, 0),      // Damaged
            41 => new(0.90f, 0.85f, 1f, 0, 0),   // Shoddy
            47 => new(1f, 1f, 1.15f, 0, 0),      // Slow
            48 => new(1f, 1f, 1.20f, 0, 0),      // Sluggish
            49 => new(1f, 1f, 1.08f, 0, 0),      // Lazy
            53 => new(1.10f, 1f, 1f, 0, 0),      // Hurtful
            54 => new(1f, 1.15f, 1f, 0, 0),      // Strong
            55 => new(1.05f, 1.15f, 1f, 0, 0),   // Unpleasant
            56 => new(1f, 0.80f, 1f, 0, 0),      // Weak
            57 => new(1.18f, 0.90f, 1f, 0, 0),   // Ruthless
            _ => default
        };
        return modifiers != default;
    }
}

/// <summary>One source-backed direct-melee item use resolved before target-specific defense/world mutation.</summary>
public readonly record struct VanillaResolvedDirectMeleeUse(
    int Damage,
    int MinimumDamage,
    int MaximumDamage,
    bool Critical,
    int CritChance,
    int AnimationTicks,
    int UseTimeTicks,
    float KnockBack,
    int ArmorPenetration,
    float ImpossibleCenterDistancePixels);

/// <summary>
/// Shared direct-melee formula consumed by both PvE and PvP strict paths. Target-specific defense and PvP
/// immunity remain downstream; tools are intentionally ordinary melee sources when their SetDefaults damage is non-zero.
/// </summary>
public static class VanillaDirectMeleeCombatMath
{
    public const int PvpMeleeCritChance = 10;

    public static VanillaResolvedDirectMeleeUse Resolve(
        in VanillaDirectMeleeCombatDefinition weapon,
        in VanillaCombatPrefixModifiers prefix,
        int damageRollPercent,
        int critRollPercent,
        bool pvp)
    {
        damageRollPercent = Math.Clamp(damageRollPercent, -15, 15);
        critRollPercent = Math.Clamp(critRollPercent, 1, 100);
        int itemDamage = Math.Max(1, (int)Math.Round(weapon.BaseDamage * prefix.DamageMultiplier));
        int minDamage = Math.Max(1, (int)Math.Round(itemDamage * 0.85f));
        int maxDamage = Math.Max(minDamage, (int)Math.Round(itemDamage * 1.15f));
        int damage = Math.Max(1, (int)Math.Round(itemDamage * (1f + damageRollPercent / 100f)));
        int critChance = pvp
            ? PvpMeleeCritChance
            : Math.Clamp(weapon.BaseCrit + prefix.CritBonus, 0, 100);
        bool critical = critRollPercent <= critChance;
        int animationTicks = Math.Max(1, (int)Math.Round(weapon.AnimationTicks * prefix.SpeedMultiplier));
        int useTimeTicks = Math.Max(1, (int)Math.Round(weapon.UseTimeTicks * prefix.SpeedMultiplier));
        float knockBack = Math.Max(0f, weapon.BaseKnockBack * prefix.KnockBackMultiplier);
        return new VanillaResolvedDirectMeleeUse(
            damage,
            minDamage,
            maxDamage,
            critical,
            critChance,
            animationTicks,
            useTimeTicks,
            knockBack,
            prefix.ArmorPenetration,
            weapon.ImpossibleCenterDistancePixels);
    }
}
