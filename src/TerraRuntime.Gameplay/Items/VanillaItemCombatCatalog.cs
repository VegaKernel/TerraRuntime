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
    float ShootSpeedMultiplier,
    int CritBonus,
    int ArmorPenetration)
{
    public static VanillaCombatPrefixModifiers Identity => new(1f, 1f, 1f, 1f, 0, 0);
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

    private static readonly VanillaDirectMeleeCombatDefinition CopperBroadsword = new(
        VanillaItemIds.CopperBroadsword,
        BaseDamage: 9,
        BaseKnockBack: 5.5f,
        BaseCrit: VanillaBaseMeleeCrit,
        UseTimeTicks: 20,
        AnimationTicks: 21,
        ImpossibleCenterDistancePixels: 192f);

    // Entire invariant direct-melee mining-tool family from original 1.4.5.8 Item.SetDefaults:
    // pick/axe/hammer > 0, melee, !noMelee and shoot == 0. Ash Wood Hammer 5283 requires owned ItemVariant.
    // These are direct-hit facts; source on-hit statuses and secondary emissions remain separate capabilities.
    private static readonly VanillaDirectMeleeCombatDefinition[] MiningTools =
    [
        new(new ItemTypeId(1), 5, 2f, VanillaBaseMeleeCrit + 0, 13, 20, 192f), // IronPickaxe
        new(new ItemTypeId(7), 7, 5.5f, VanillaBaseMeleeCrit + 0, 20, 30, 192f), // IronHammer
        new(new ItemTypeId(10), 5, 4.5f, VanillaBaseMeleeCrit + 0, 19, 27, 192f), // IronAxe
        new(new ItemTypeId(45), 20, 6f, VanillaBaseMeleeCrit + 0, 15, 30, 192f), // WarAxeoftheNight
        new(new ItemTypeId(103), 9, 3f, VanillaBaseMeleeCrit + 0, 15, 20, 192f), // NightmarePickaxe
        new(new ItemTypeId(104), 24, 6f, VanillaBaseMeleeCrit + 0, 19, 45, 192f), // TheBreaker
        new(new ItemTypeId(122), 12, 2f, VanillaBaseMeleeCrit + 0, 18, 23, 192f), // MoltenPickaxe
        new(new ItemTypeId(196), 2, 5.5f, VanillaBaseMeleeCrit + 0, 25, 37, 192f), // WoodenHammer
        new(new ItemTypeId(204), 20, 7f, VanillaBaseMeleeCrit + 0, 16, 30, 192f), // MeteorHamaxe
        new(new ItemTypeId(217), 20, 7f, VanillaBaseMeleeCrit + 0, 14, 27, 192f), // MoltenHamaxe
        new(new ItemTypeId(367), 26, 7.5f, VanillaBaseMeleeCrit + 0, 14, 27, 192f), // Pwnhammer
        new(new ItemTypeId(654), 7, 5.5f, VanillaBaseMeleeCrit + 0, 20, 30, 192f), // EbonwoodHammer
        new(new ItemTypeId(657), 4, 5.5f, VanillaBaseMeleeCrit + 0, 23, 33, 192f), // RichMahoganyHammer
        new(new ItemTypeId(660), 10, 5.5f, VanillaBaseMeleeCrit + 0, 19, 29, 192f), // PearlwoodHammer
        new(new ItemTypeId(776), 10, 5f, VanillaBaseMeleeCrit + 0, 13, 25, 192f), // CobaltPickaxe
        new(new ItemTypeId(777), 15, 5f, VanillaBaseMeleeCrit + 0, 10, 25, 192f), // MythrilPickaxe
        new(new ItemTypeId(778), 20, 5f, VanillaBaseMeleeCrit + 0, 8, 25, 192f), // AdamantitePickaxe
        new(new ItemTypeId(787), 26, 7.5f, VanillaBaseMeleeCrit + 0, 14, 27, 192f), // Hammush
        new(new ItemTypeId(797), 23, 6f, VanillaBaseMeleeCrit + 0, 19, 40, 192f), // FleshGrinder
        new(new ItemTypeId(798), 12, 3.5f, VanillaBaseMeleeCrit + 0, 14, 22, 192f), // DeathbringerPickaxe
        new(new ItemTypeId(799), 22, 6f, VanillaBaseMeleeCrit + 0, 15, 32, 192f), // BloodLustCluster
        new(new ItemTypeId(882), 4, 2f, VanillaBaseMeleeCrit + 0, 16, 25, 192f), // CactusPickaxe
        new(new ItemTypeId(922), 7, 5.5f, VanillaBaseMeleeCrit + 0, 20, 30, 192f), // ShadewoodHammer
        new(new ItemTypeId(990), 35, 4.75f, VanillaBaseMeleeCrit + 0, 7, 25, 192f), // PickaxeAxe
        new(new ItemTypeId(991), 33, 5f, VanillaBaseMeleeCrit + 0, 13, 35, 192f), // CobaltWaraxe
        new(new ItemTypeId(992), 39, 6f, VanillaBaseMeleeCrit + 0, 10, 35, 192f), // MythrilWaraxe
        new(new ItemTypeId(993), 43, 7f, VanillaBaseMeleeCrit + 0, 8, 35, 192f), // AdamantiteWaraxe
        new(new ItemTypeId(1188), 12, 5f, VanillaBaseMeleeCrit + 0, 12, 25, 192f), // PalladiumPickaxe
        new(new ItemTypeId(1195), 17, 5f, VanillaBaseMeleeCrit + 0, 9, 25, 192f), // OrichalcumPickaxe
        new(new ItemTypeId(1202), 27, 5f, VanillaBaseMeleeCrit + 0, 7, 25, 192f), // TitaniumPickaxe
        new(new ItemTypeId(1222), 36, 5.5f, VanillaBaseMeleeCrit + 0, 12, 35, 192f), // PalladiumWaraxe
        new(new ItemTypeId(1223), 41, 6.5f, VanillaBaseMeleeCrit + 0, 9, 35, 192f), // OrichalcumWaraxe
        new(new ItemTypeId(1224), 44, 7.5f, VanillaBaseMeleeCrit + 0, 7, 35, 192f), // TitaniumWaraxe
        new(new ItemTypeId(1230), 40, 5f, VanillaBaseMeleeCrit + 0, 7, 25, 192f), // ChlorophytePickaxe
        new(new ItemTypeId(1233), 70, 7f, VanillaBaseMeleeCrit + 0, 7, 30, 192f), // ChlorophyteGreataxe
        new(new ItemTypeId(1234), 80, 8f, VanillaBaseMeleeCrit + 0, 14, 35, 192f), // ChlorophyteWarhammer
        new(new ItemTypeId(1294), 34, 5.5f, VanillaBaseMeleeCrit + 0, 6, 16, 192f), // Picksaw
        new(new ItemTypeId(1305), 72, 7.25f, VanillaBaseMeleeCrit + 0, 7, 23, 192f), // TheAxe
        new(new ItemTypeId(1320), 8, 3f, VanillaBaseMeleeCrit + 0, 11, 19, 192f), // BonePickaxe
        new(new ItemTypeId(1506), 32, 5.25f, VanillaBaseMeleeCrit + 0, 8, 24, 192f), // SpectrePickaxe
        new(new ItemTypeId(1507), 60, 7f, VanillaBaseMeleeCrit + 0, 8, 28, 192f), // SpectreHamaxe
        new(new ItemTypeId(1917), 7, 2.5f, VanillaBaseMeleeCrit + 0, 16, 20, 192f), // CnadyCanePickaxe
        new(new ItemTypeId(2176), 45, 6f, VanillaBaseMeleeCrit + 0, 4, 12, 192f), // ShroomiteDiggingClaw
        new(new ItemTypeId(2320), 24, 6f, VanillaBaseMeleeCrit + 0, 14, 24, 192f), // Rockfish
        new(new ItemTypeId(2341), 16, 3f, VanillaBaseMeleeCrit + 0, 13, 22, 192f), // ReaverShark
        new(new ItemTypeId(2516), 4, 5.5f, VanillaBaseMeleeCrit + 0, 23, 33, 192f), // PalmWoodHammer
        new(new ItemTypeId(2746), 4, 5.5f, VanillaBaseMeleeCrit + 0, 23, 33, 192f), // BorealWoodHammer
        new(new ItemTypeId(2776), 80, 5.5f, VanillaBaseMeleeCrit + 0, 6, 12, 192f), // VortexPickaxe
        new(new ItemTypeId(2781), 80, 5.5f, VanillaBaseMeleeCrit + 0, 6, 12, 192f), // NebulaPickaxe
        new(new ItemTypeId(2786), 80, 5.5f, VanillaBaseMeleeCrit + 0, 6, 12, 192f), // SolarFlarePickaxe
        new(new ItemTypeId(3466), 80, 5.5f, VanillaBaseMeleeCrit + 0, 6, 12, 192f), // StardustPickaxe
        new(new ItemTypeId(3481), 10, 5.5f, VanillaBaseMeleeCrit + 0, 21, 27, 192f), // PlatinumHammer
        new(new ItemTypeId(3482), 8, 4.5f, VanillaBaseMeleeCrit + 0, 17, 25, 192f), // PlatinumAxe
        new(new ItemTypeId(3485), 7, 2f, VanillaBaseMeleeCrit + 0, 15, 19, 192f), // PlatinumPickaxe
        new(new ItemTypeId(3487), 9, 5.5f, VanillaBaseMeleeCrit + 0, 25, 28, 192f), // TungstenHammer
        new(new ItemTypeId(3488), 7, 4.5f, VanillaBaseMeleeCrit + 0, 18, 26, 192f), // TungstenAxe
        new(new ItemTypeId(3491), 6, 2f, VanillaBaseMeleeCrit + 0, 19, 21, 192f), // TungstenPickaxe
        new(new ItemTypeId(3493), 8, 5.5f, VanillaBaseMeleeCrit + 0, 19, 29, 192f), // LeadHammer
        new(new ItemTypeId(3494), 6, 4.5f, VanillaBaseMeleeCrit + 0, 19, 28, 192f), // LeadAxe
        new(new ItemTypeId(3497), 6, 2f, VanillaBaseMeleeCrit + 0, 12, 19, 192f), // LeadPickaxe
        new(new ItemTypeId(3499), 6, 5.5f, VanillaBaseMeleeCrit + 0, 21, 31, 192f), // TinHammer
        new(new ItemTypeId(3500), 4, 4.5f, VanillaBaseMeleeCrit + 0, 20, 28, 192f), // TinAxe
        new(new ItemTypeId(3503), 5, 2f, VanillaBaseMeleeCrit + 0, 14, 21, 192f), // TinPickaxe
        new(new ItemTypeId(3505), 4, 5.5f, VanillaBaseMeleeCrit + 0, 23, 33, 192f), // CopperHammer
        new(new ItemTypeId(3506), 3, 4.5f, VanillaBaseMeleeCrit + 0, 21, 30, 192f), // CopperAxe
        new(new ItemTypeId(3509), 4, 2f, VanillaBaseMeleeCrit + 0, 15, 23, 192f), // CopperPickaxe
        new(new ItemTypeId(3511), 9, 5.5f, VanillaBaseMeleeCrit + 0, 19, 29, 192f), // SilverHammer
        new(new ItemTypeId(3512), 6, 4.5f, VanillaBaseMeleeCrit + 0, 18, 26, 192f), // SilverAxe
        new(new ItemTypeId(3515), 6, 2f, VanillaBaseMeleeCrit + 0, 11, 19, 192f), // SilverPickaxe
        new(new ItemTypeId(3517), 9, 5.5f, VanillaBaseMeleeCrit + 0, 23, 28, 192f), // GoldHammer
        new(new ItemTypeId(3518), 7, 4.5f, VanillaBaseMeleeCrit + 0, 18, 26, 192f), // GoldAxe
        new(new ItemTypeId(3521), 6, 2f, VanillaBaseMeleeCrit + 0, 17, 20, 192f), // GoldPickaxe
        new(new ItemTypeId(3522), 60, 7f, VanillaBaseMeleeCrit + 0, 7, 28, 192f), // LunarHamaxeSolar
        new(new ItemTypeId(3523), 60, 7f, VanillaBaseMeleeCrit + 0, 7, 28, 192f), // LunarHamaxeVortex
        new(new ItemTypeId(3524), 60, 7f, VanillaBaseMeleeCrit + 0, 7, 28, 192f), // LunarHamaxeNebula
        new(new ItemTypeId(3525), 60, 7f, VanillaBaseMeleeCrit + 0, 7, 28, 192f), // LunarHamaxeStardust
        new(new ItemTypeId(4059), 8, 4f, VanillaBaseMeleeCrit + 0, 14, 18, 192f), // FossilPickaxe
        new(new ItemTypeId(4317), 30, 7f, VanillaBaseMeleeCrit + 0, 11, 27, 192f), // BloodHamaxe
        new(new ItemTypeId(5095), 27, 5f, VanillaBaseMeleeCrit + 10, 15, 15, 192f), // LucyTheAxe
        new(new ItemTypeId(5295), 20, 5f, VanillaBaseMeleeCrit + 0, 12, 24, 192f), // AcornAxe
    ];

    // Wood and metal broadswords with invariant Item.SetDefaults bodies, melee contact and no shoot.
    // Copper remains in its existing entry. Ash Wood Sword requires the world-owned Remix ItemVariant.
    private static readonly VanillaDirectMeleeCombatDefinition[] Broadswords =
    [
        new(new ItemTypeId(4), 12, 5.5f, VanillaBaseMeleeCrit, 20, 20, 192f), // IronBroadsword
        new(new ItemTypeId(24), 7, 5f, VanillaBaseMeleeCrit, 20, 20, 192f), // WoodenSword
        new(new ItemTypeId(482), 61, 6f, VanillaBaseMeleeCrit, 21, 21, 192f), // AdamantiteSword
        new(new ItemTypeId(483), 40, 5f, VanillaBaseMeleeCrit, 19, 19, 192f), // CobaltSword
        new(new ItemTypeId(484), 50, 6f, VanillaBaseMeleeCrit, 20, 20, 192f), // MythrilSword
        new(new ItemTypeId(653), 11, 6f, VanillaBaseMeleeCrit, 19, 19, 192f), // EbonwoodSword
        new(new ItemTypeId(656), 8, 6f, VanillaBaseMeleeCrit, 19, 19, 192f), // RichMahoganySword
        new(new ItemTypeId(659), 30, 7f, VanillaBaseMeleeCrit, 15, 15, 192f), // PearlwoodSword
        new(new ItemTypeId(921), 11, 6f, VanillaBaseMeleeCrit, 19, 19, 192f), // ShadewoodSword
        new(new ItemTypeId(1185), 49, 5.5f, VanillaBaseMeleeCrit, 22, 22, 192f), // PalladiumSword
        new(new ItemTypeId(1192), 59, 6f, VanillaBaseMeleeCrit, 22, 22, 192f), // OrichalcumSword
        new(new ItemTypeId(1199), 61, 6f, VanillaBaseMeleeCrit, 20, 20, 192f), // TitaniumSword
        new(new ItemTypeId(2517), 8, 6f, VanillaBaseMeleeCrit, 19, 19, 192f), // PalmWoodSword
        new(new ItemTypeId(2745), 8, 6f, VanillaBaseMeleeCrit, 20, 20, 192f), // BorealWoodSword
        new(new ItemTypeId(3484), 16, 6.5f, VanillaBaseMeleeCrit, 17, 17, 192f), // PlatinumBroadsword
        new(new ItemTypeId(3490), 14, 6f, VanillaBaseMeleeCrit, 19, 19, 192f), // TungstenBroadsword
        new(new ItemTypeId(3496), 13, 5.5f, VanillaBaseMeleeCrit, 20, 20, 192f), // LeadBroadsword
        new(new ItemTypeId(3502), 10, 5.5f, VanillaBaseMeleeCrit, 20, 20, 192f), // TinBroadsword
        new(new ItemTypeId(3514), 14, 6f, VanillaBaseMeleeCrit, 20, 20, 192f), // SilverBroadsword
        new(new ItemTypeId(3520), 15, 6.5f, VanillaBaseMeleeCrit, 18, 18, 192f), // GoldBroadsword
    ];

    public static bool TryGetDirectMelee(ItemTypeId type, out VanillaDirectMeleeCombatDefinition definition)
    {
        if (type == VanillaItemIds.Muramasa)
        {
            definition = Muramasa;
            return true;
        }
        if (type == VanillaItemIds.CopperBroadsword)
        {
            definition = CopperBroadsword;
            return true;
        }

        for (int i = 0; i < MiningTools.Length; i++)
        {
            if (MiningTools[i].Type == type)
            {
                definition = MiningTools[i];
                return true;
            }
        }

        for (int i = 0; i < Broadswords.Length; i++)
        {
            if (Broadswords[i].Type == type)
            {
                definition = Broadswords[i];
                return true;
            }
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
            38 => new(1f, 1.15f, 1f, 1f, 0, 0),       // Forceful
            39 => new(0.70f, 0.80f, 1f, 1f, 0, 0),   // Broken
            40 => new(0.85f, 1f, 1f, 1f, 0, 0),      // Damaged
            41 => new(0.90f, 0.85f, 1f, 1f, 0, 0),   // Shoddy
            47 => new(1f, 1f, 1.15f, 1f, 0, 0),      // Slow
            48 => new(1f, 1f, 1.20f, 1f, 0, 0),      // Sluggish
            49 => new(1f, 1f, 1.08f, 1f, 0, 0),      // Lazy
            53 => new(1.10f, 1f, 1f, 1f, 0, 0),      // Hurtful
            54 => new(1f, 1.15f, 1f, 1f, 0, 0),      // Strong
            55 => new(1.05f, 1.15f, 1f, 1f, 0, 0),   // Unpleasant
            56 => new(1f, 0.80f, 1f, 1f, 0, 0),      // Weak
            57 => new(1.18f, 0.90f, 1f, 1f, 0, 0),   // Ruthless
            _ => default
        };
        return modifiers != default;
    }

    /// <summary>Exact ranged-prefix multipliers used by ordinary bows in Item.TryGetPrefixStatMultipliersForItem.</summary>
    public static bool TryGetRangedPrefixModifiers(PrefixId prefix, out VanillaCombatPrefixModifiers modifiers)
    {
        if (prefix == VanillaPrefixIds.None)
        {
            modifiers = VanillaCombatPrefixModifiers.Identity;
            return true;
        }

        modifiers = prefix.Value switch
        {
            16 => new(1.10f, 1f, 1f, 1f, 3, 0),
            17 => new(1f, 1f, 0.85f, 1.10f, 0, 0),
            18 => new(1f, 1f, 0.90f, 1.15f, 0, 0),
            19 => new(1f, 1.15f, 1f, 1.05f, 0, 0),
            20 => new(1.10f, 1.05f, 0.95f, 1.05f, 2, 0),
            21 => new(1.10f, 1.15f, 1f, 1f, 0, 0),
            22 => new(0.85f, 0.90f, 1f, 0.90f, 0, 0),
            23 => new(1f, 1f, 1.15f, 0.90f, 0, 0),
            24 => new(1f, 0.80f, 1.10f, 1f, 0, 0),
            25 => new(1.15f, 1f, 1.10f, 1f, 1, 0),
            82 => new(1.15f, 1.15f, 0.90f, 1.10f, 5, 0),
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
        in VanillaPlayerCombatSnapshot attacker,
        int damageRollPercent,
        int critRollPercent,
        bool pvp)
    {
        damageRollPercent = Math.Clamp(damageRollPercent, -15, 15);
        critRollPercent = Math.Clamp(critRollPercent, 1, 100);
        // Item.Prefix rounds item-local damage first. Player.GetWeaponDamage truncates after the player-wide
        // class multiplier (+5E-06f is copied from the pinned source). Main.DamageVar rounds last.
        int prefixedItemDamage = Math.Max(1, (int)Math.Round(weapon.BaseDamage * prefix.DamageMultiplier));
        int itemDamage = Math.Max(1, (int)(prefixedItemDamage * attacker.MeleeDamage + 5E-06f));
        int minDamage = Math.Max(1, (int)Math.Round(itemDamage * 0.85f));
        int maxDamage = Math.Max(minDamage, (int)Math.Round(itemDamage * 1.15f));
        int damage = Math.Max(1, (int)Math.Round(itemDamage * (1f + damageRollPercent / 100f)));
        int critChance = pvp
            ? PvpMeleeCritChance
            : Math.Clamp(attacker.MeleeCrit + weapon.BaseCrit - VanillaItemCombatCatalog.VanillaBaseMeleeCrit + prefix.CritBonus, 0, 100);
        bool critical = critRollPercent <= critChance;
        int prefixedAnimation = Math.Max(1, (int)Math.Round(weapon.AnimationTicks * prefix.SpeedMultiplier));
        int prefixedUseTime = Math.Max(1, (int)Math.Round(weapon.UseTimeTicks * prefix.SpeedMultiplier));
        // ApplyItemAnimation(baseFrames, meleeSpeed) truncates rather than rounds after CapAttackSpeeds.
        int animationTicks = Math.Max(1, (int)(prefixedAnimation * attacker.MeleeAnimationMultiplier));
        float knockBack = Math.Max(0f, weapon.BaseKnockBack * prefix.KnockBackMultiplier);
        return new VanillaResolvedDirectMeleeUse(
            damage,
            minDamage,
            maxDamage,
            critical,
            critChance,
            animationTicks,
            prefixedUseTime,
            knockBack,
            checked(attacker.GetArmorPenetration(melee: true) + prefix.ArmorPenetration),
            weapon.ImpossibleCenterDistancePixels);
    }
}
