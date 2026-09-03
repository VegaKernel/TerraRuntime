using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Source-backed combat projection of Player.ResetEffects + UpdateEquips + GrantArmorBenefits + GrantPrefixBenefits
/// from TerrariaServer 1.4.5.8. Unknown active combat equipment fails closed instead of being approximated.
/// Vanity, dye, misc and inactive loadout slots are intentionally not combat inputs.
/// </summary>
public readonly record struct VanillaPlayerCombatSnapshot(
    int Defense,
    float Endurance,
    float MeleeDamage,
    float RangedDamage,
    float MagicDamage,
    float RangedMultDamage,
    float ArrowDamage,
    float BulletDamage,
    float ArrowDamageAdditiveStack,
    int MeleeCrit,
    int RangedCrit,
    int MagicCrit,
    float MeleeAttackSpeed,
    int ArmorPenetration,
    int MeleeArmorPenetration,
    bool NoKnockback,
    bool MeleeKnockbackGlove,
    bool Archery,
    bool AmmoPotion,
    bool MagicQuiver,
    bool MoltenQuiver,
    bool MagmaStone,
    bool AmmoCost80,
    bool MysticSashDodge,
    bool BlackBeltDodge,
    bool BrainOfConfusionDodge,
    bool BrainOfConfusionCooldown,
    bool ShadowDodge,
    bool LongInvincibility)
{
    public static VanillaPlayerCombatSnapshot Baseline => new(
        Defense: 0,
        Endurance: 0f,
        MeleeDamage: 1f,
        RangedDamage: 1f,
        MagicDamage: 1f,
        RangedMultDamage: 1f,
        ArrowDamage: 1f,
        BulletDamage: 1f,
        ArrowDamageAdditiveStack: 0f,
        MeleeCrit: VanillaItemCombatCatalog.VanillaBaseMeleeCrit,
        RangedCrit: 4,
        MagicCrit: 4,
        MeleeAttackSpeed: 1f,
        ArmorPenetration: 0,
        MeleeArmorPenetration: 0,
        NoKnockback: false,
        MeleeKnockbackGlove: false,
        Archery: false,
        AmmoPotion: false,
        MagicQuiver: false,
        MoltenQuiver: false,
        MagmaStone: false,
        AmmoCost80: false,
        MysticSashDodge: false,
        BlackBeltDodge: false,
        BrainOfConfusionDodge: false,
        BrainOfConfusionCooldown: false,
        ShadowDodge: false,
        LongInvincibility: false);

    /// <summary>Player.CapAttackSpeeds / TurnAttackSpeedToUseTimeMultiplier.</summary>
    public float MeleeAnimationMultiplier
    {
        get
        {
            float speed = Math.Min(MeleeAttackSpeed, 3f);
            return speed == 0f ? 0f : 1f / speed;
        }
    }

    /// <summary>Player.bowEffectiveDamage from the pinned server source.</summary>
    public float BowDamageMultiplier =>
        (RangedDamage / RangedMultDamage + ArrowDamageAdditiveStack) * RangedMultDamage * ArrowDamage;

    /// <summary>Player.gunEffectiveDamage from the pinned server source.</summary>
    public float GunDamageMultiplier => RangedDamage * BulletDamage;

    public float MeleeKnockBackMultiplier => MeleeKnockbackGlove ? 2f : 1f;

    public int GetArmorPenetration(bool melee) =>
        ArmorPenetration + (melee ? MeleeArmorPenetration : 0);
}

public static class VanillaPlayerCombatEquipmentCatalog
{
    public static bool TryBuild(
        ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        out VanillaPlayerCombatSnapshot snapshot)
    {
        snapshot = VanillaPlayerCombatSnapshot.Baseline;
        ItemTypeId head = VanillaItemIds.None;
        ItemTypeId body = VanillaItemIds.None;
        ItemTypeId legs = VanillaItemIds.None;
        bool skyStoneEffects = false;

        for (int i = 0; i < equipment.Length; i++)
        {
            PlayerEquipmentCommitRequest request = equipment[i];
            if (!VanillaPlayerItemSlotCatalog.IsFunctionalArmorSlot(request.SlotId))
                continue;

            // Slots 8/9 depend on Demon Heart / Master-mode unlock state. That state is not yet part of the
            // authoritative combat snapshot, so a non-empty item there remains outside the trusted slice.
            if (!VanillaPlayerItemSlotCatalog.IsBaselineFunctionalArmorSlot(request.SlotId))
                return false;
            if (!request.TryGetCanonicalItemType(out ItemTypeId type) || type.IsNone)
                return false;

            int armorIndex = request.SlotId - VanillaPlayerItemSlotCatalog.ArmorStart;
            if (armorIndex is >= 0 and <= 2)
            {
                if (request.PrefixId != VanillaPrefixIds.None ||
                    !TryApplyArmorPiece(type, armorIndex, ref snapshot))
                {
                    return false;
                }

                if (armorIndex == 0) head = type;
                else if (armorIndex == 1) body = type;
                else legs = type;
                continue;
            }

            if (!TryApplyAccessory(type, request.PrefixId, ref snapshot, ref skyStoneEffects))
                return false;
        }

        ApplyArmorSetBonus(head, body, legs, ref snapshot);
        if (skyStoneEffects)
        {
            // Player.UpdateEquips applies this once through the skyStoneEffects boolean even when more than one
            // source item set the flag.
            snapshot = snapshot with
            {
                Defense = snapshot.Defense + 4,
                MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.1f,
                MeleeDamage = snapshot.MeleeDamage + 0.1f,
                RangedDamage = snapshot.RangedDamage + 0.1f,
                MagicDamage = snapshot.MagicDamage + 0.1f,
                MeleeCrit = snapshot.MeleeCrit + 2,
                RangedCrit = snapshot.RangedCrit + 2,
                MagicCrit = snapshot.MagicCrit + 2
            };
        }

        return true;
    }

    private static bool TryApplyArmorPiece(
        ItemTypeId type,
        int armorIndex,
        ref VanillaPlayerCombatSnapshot snapshot)
    {
        int requiredIndex;
        int defense;
        switch (type.Value)
        {
            // Metal armor families. They have no per-piece combat modifier beyond defense.
            case 89: requiredIndex = 0; defense = 1; break; // Copper Helmet
            case 80: requiredIndex = 1; defense = 2; break;
            case 76: requiredIndex = 2; defense = 1; break;
            case 687: requiredIndex = 0; defense = 2; break; // Tin
            case 688: requiredIndex = 1; defense = 2; break;
            case 689: requiredIndex = 2; defense = 1; break;
            case 90: case 954: requiredIndex = 0; defense = 2; break; // Iron / Ancient Iron helmet
            case 81: requiredIndex = 1; defense = 3; break;
            case 77: requiredIndex = 2; defense = 2; break;
            case 91: requiredIndex = 0; defense = 3; break; // Silver
            case 82: requiredIndex = 1; defense = 4; break;
            case 78: requiredIndex = 2; defense = 3; break;
            case 92: case 955: requiredIndex = 0; defense = 4; break; // Gold / Ancient Gold helmet
            case 83: requiredIndex = 1; defense = 5; break;
            case 79: requiredIndex = 2; defense = 4; break;
            case 690: requiredIndex = 0; defense = 3; break; // Lead
            case 691: requiredIndex = 1; defense = 3; break;
            case 692: requiredIndex = 2; defense = 2; break;
            case 693: requiredIndex = 0; defense = 4; break; // Tungsten
            case 694: requiredIndex = 1; defense = 5; break;
            case 695: requiredIndex = 2; defense = 3; break;
            case 696: requiredIndex = 0; defense = 5; break; // Platinum
            case 697: requiredIndex = 1; defense = 6; break;
            case 698: requiredIndex = 2; defense = 5; break;

            case 1731: requiredIndex = 0; defense = 2; break; // Pumpkin
            case 1732: requiredIndex = 1; defense = 3; break;
            case 1733: requiredIndex = 2; defense = 2; break;
            case 3187: requiredIndex = 0; defense = 5; break; // Gladiator
            case 3188: requiredIndex = 1; defense = 6; break;
            case 3189: requiredIndex = 2; defense = 5; break;

            case 256: requiredIndex = 0; defense = 2; break; // Ninja
            case 257: requiredIndex = 1; defense = 4; break;
            case 258: requiredIndex = 2; defense = 3; break;
            case 3374: requiredIndex = 0; defense = 4; break; // Fossil
            case 3375: requiredIndex = 1; defense = 5; break;
            case 3376: requiredIndex = 2; defense = 4; break;
            case 151: case 959: requiredIndex = 0; defense = 6; break; // Necro / Ancient Necro helmet
            case 152: requiredIndex = 1; defense = 7; break;
            case 153: requiredIndex = 2; defense = 6; break;
            case 102: case 956: requiredIndex = 0; defense = 6; break; // Shadow / Ancient Shadow
            case 101: case 957: requiredIndex = 1; defense = 7; break;
            case 100: case 958: requiredIndex = 2; defense = 6; break;
            case 792: requiredIndex = 0; defense = 6; break; // Crimson
            case 793: requiredIndex = 1; defense = 7; break;
            case 794: requiredIndex = 2; defense = 6; break;
            case 123: requiredIndex = 0; defense = 5; break; // Meteor
            case 124: requiredIndex = 1; defense = 6; break;
            case 125: requiredIndex = 2; defense = 5; break;
            case 228: case 960: requiredIndex = 0; defense = 5; break; // Jungle / Ancient Cobalt
            case 229: case 961: requiredIndex = 1; defense = 6; break;
            case 230: case 962: requiredIndex = 2; defense = 6; break;
            case 231: requiredIndex = 0; defense = 8; break; // Molten
            case 232: requiredIndex = 1; defense = 9; break;
            case 233: requiredIndex = 2; defense = 8; break;
            default: return false;
        }

        if (armorIndex != requiredIndex)
            return false;
        snapshot = snapshot with { Defense = snapshot.Defense + defense };

        switch (type.Value)
        {
            case 256: case 257: case 258:
                snapshot = snapshot with
                {
                    MeleeCrit = snapshot.MeleeCrit + 3,
                    RangedCrit = snapshot.RangedCrit + 3,
                    MagicCrit = snapshot.MagicCrit + 3
                };
                break;
            case 3374:
                snapshot = snapshot with { RangedCrit = snapshot.RangedCrit + 4 };
                break;
            case 3375:
                snapshot = snapshot with { RangedDamage = snapshot.RangedDamage + 0.05f };
                break;
            case 3376:
                snapshot = snapshot with { RangedCrit = snapshot.RangedCrit + 4 };
                break;
            case 151: case 959: case 152: case 153:
                snapshot = snapshot with { RangedDamage = snapshot.RangedDamage + 0.05f };
                break;
            case 123: case 124: case 125:
                snapshot = snapshot with { MagicDamage = snapshot.MagicDamage + 0.09f };
                break;
            case 228: case 960:
                snapshot = snapshot with { MagicCrit = snapshot.MagicCrit + 6 };
                break;
            case 229: case 961:
                snapshot = snapshot with { MagicDamage = snapshot.MagicDamage + 0.06f };
                break;
            case 230: case 962:
                snapshot = snapshot with { MagicCrit = snapshot.MagicCrit + 6 };
                break;
            case 100: case 101: case 102: case 956: case 957: case 958:
                snapshot = snapshot with
                {
                    MeleeCrit = snapshot.MeleeCrit + 5,
                    RangedCrit = snapshot.RangedCrit + 5,
                    MagicCrit = snapshot.MagicCrit + 5
                };
                break;
            case 792: case 793: case 794:
                snapshot = snapshot with
                {
                    MeleeDamage = snapshot.MeleeDamage + 0.03f,
                    RangedDamage = snapshot.RangedDamage + 0.03f,
                    MagicDamage = snapshot.MagicDamage + 0.03f
                };
                break;
            case 231:
                snapshot = snapshot with { MeleeCrit = snapshot.MeleeCrit + 7 };
                break;
            case 232:
                snapshot = snapshot with { MeleeDamage = snapshot.MeleeDamage + 0.07f };
                break;
            case 233:
                snapshot = snapshot with { MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.07f };
                break;
        }
        return true;
    }

    private static void ApplyArmorSetBonus(
        ItemTypeId head,
        ItemTypeId body,
        ItemTypeId legs,
        ref VanillaPlayerCombatSnapshot snapshot)
    {
        // ArmorSetBonuses.Initialize / Benefits in the pinned source.
        if ((head.Value is 89 or 687 or 90 or 954) &&
            ((head.Value == 89 && body.Value == 80 && legs.Value == 76) ||
             (head.Value == 687 && body.Value == 688 && legs.Value == 689) ||
             (head.Value is 90 or 954 && body.Value == 81 && legs.Value == 77)))
        {
            snapshot = snapshot with { Defense = snapshot.Defense + 2 };
        }
        else if ((head.Value == 91 && body.Value == 82 && legs.Value == 78) ||
                 (head.Value is 92 or 955 && body.Value == 83 && legs.Value == 79) ||
                 (head.Value == 690 && body.Value == 691 && legs.Value == 692) ||
                 (head.Value == 693 && body.Value == 694 && legs.Value == 695))
        {
            snapshot = snapshot with { Defense = snapshot.Defense + 3 };
        }
        else if (head.Value == 696 && body.Value == 697 && legs.Value == 698)
        {
            snapshot = snapshot with { Defense = snapshot.Defense + 4 };
        }
        else if (head.Value == 1731 && body.Value == 1732 && legs.Value == 1733)
        {
            snapshot = snapshot with
            {
                MeleeDamage = snapshot.MeleeDamage + 0.1f,
                RangedDamage = snapshot.RangedDamage + 0.1f,
                MagicDamage = snapshot.MagicDamage + 0.1f
            };
        }
        else if (head.Value == 3187 && body.Value == 3188 && legs.Value == 3189)
        {
            snapshot = snapshot with { NoKnockback = true };
        }
        else if (head.Value == 3374 && body.Value == 3375 && legs.Value == 3376)
        {
            snapshot = snapshot with { AmmoCost80 = true };
        }
        else if (head.Value is 151 or 959 && body.Value == 152 && legs.Value == 153)
        {
            snapshot = snapshot with { RangedCrit = snapshot.RangedCrit + 10 };
        }
        else if (head.Value == 231 && body.Value == 232 && legs.Value == 233)
        {
            snapshot = snapshot with { MeleeDamage = snapshot.MeleeDamage + 0.1f };
        }
        // Ninja, Shadow, Crimson, Meteor and Jungle full-set bonuses do not change the currently modeled
        // melee/ranged damage/crit/defense/knockback inputs beyond their per-piece effects above.
    }

    private static bool TryApplyAccessory(
        ItemTypeId type,
        PrefixId prefix,
        ref VanillaPlayerCombatSnapshot snapshot,
        ref bool skyStoneEffects)
    {
        switch (type.Value)
        {
            case 156: // Cobalt Shield
                snapshot = snapshot with { Defense = snapshot.Defense + 1, NoKnockback = true };
                break;
            case 554: // Cross Necklace: dodge methods use longInvince when setting their post-dodge immunity.
                snapshot = snapshot with { LongInvincibility = true };
                break;
            case 963: // Black Belt
                snapshot = snapshot with { BlackBeltDodge = true };
                break;
            case 984: // Master Ninja Gear
                snapshot = snapshot with { BlackBeltDodge = true };
                break;
            case 3223: // Brain of Confusion
                snapshot = snapshot with { BrainOfConfusionDodge = true };
                break;
            case 6189: // Mystic Sash
                snapshot = snapshot with { MysticSashDodge = true };
                break;
            case 211: // Feral Claws
                snapshot = snapshot with { MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.12f };
                break;
            case 536: // Titan Glove
                snapshot = snapshot with { MeleeKnockbackGlove = true };
                break;
            case 897: // Power Glove
                snapshot = snapshot with
                {
                    MeleeKnockbackGlove = true,
                    MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.12f
                };
                break;
            case 936: // Mechanical Glove
                snapshot = snapshot with
                {
                    MeleeKnockbackGlove = true,
                    MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.12f,
                    MeleeDamage = snapshot.MeleeDamage + 0.12f
                };
                break;
            case 1343: // Fire Gauntlet
                snapshot = snapshot with
                {
                    MeleeKnockbackGlove = true,
                    MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.12f,
                    MeleeDamage = snapshot.MeleeDamage + 0.12f,
                    MagmaStone = true
                };
                break;
            case 1322: // Magma Stone
                snapshot = snapshot with { MagmaStone = true };
                break;
            case 3992: // Berserker's Glove
                snapshot = snapshot with
                {
                    Defense = snapshot.Defense + 8,
                    MeleeKnockbackGlove = true,
                    MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.12f
                };
                break;
            case 489: // Sorcerer Emblem
                snapshot = snapshot with { MagicDamage = snapshot.MagicDamage + 0.15f };
                break;
            case 490: // Warrior Emblem
                snapshot = snapshot with { MeleeDamage = snapshot.MeleeDamage + 0.15f };
                break;
            case 491: // Ranger Emblem
                snapshot = snapshot with { RangedDamage = snapshot.RangedDamage + 0.15f };
                break;
            case 935: // Avenger Emblem
                snapshot = snapshot with
                {
                    MeleeDamage = snapshot.MeleeDamage + 0.12f,
                    RangedDamage = snapshot.RangedDamage + 0.12f,
                    MagicDamage = snapshot.MagicDamage + 0.12f
                };
                break;
            case 1301: // Destroyer Emblem
                snapshot = snapshot with
                {
                    MeleeDamage = snapshot.MeleeDamage + 0.1f,
                    RangedDamage = snapshot.RangedDamage + 0.1f,
                    MagicDamage = snapshot.MagicDamage + 0.1f,
                    MeleeCrit = snapshot.MeleeCrit + 8,
                    RangedCrit = snapshot.RangedCrit + 8,
                    MagicCrit = snapshot.MagicCrit + 8
                };
                break;
            case 1858: // Sniper Scope
                snapshot = snapshot with
                {
                    RangedDamage = snapshot.RangedDamage + 0.1f,
                    RangedCrit = snapshot.RangedCrit + 10
                };
                break;
            case 2220: // Celestial Emblem
                snapshot = snapshot with { MagicDamage = snapshot.MagicDamage + 0.15f };
                break;
            case 3015: // Putrid Scent
                snapshot = snapshot with
                {
                    MeleeDamage = snapshot.MeleeDamage + 0.05f,
                    RangedDamage = snapshot.RangedDamage + 0.05f,
                    MagicDamage = snapshot.MagicDamage + 0.05f,
                    MeleeCrit = snapshot.MeleeCrit + 5,
                    RangedCrit = snapshot.RangedCrit + 5,
                    MagicCrit = snapshot.MagicCrit + 5
                };
                break;
            case 1321: // Magic Quiver
                snapshot = snapshot with
                {
                    MagicQuiver = true,
                    ArrowDamageAdditiveStack = snapshot.ArrowDamageAdditiveStack + 0.1f
                };
                break;
            case 4002: // Molten Quiver
                snapshot = snapshot with
                {
                    MagicQuiver = true,
                    MoltenQuiver = true,
                    ArrowDamageAdditiveStack = snapshot.ArrowDamageAdditiveStack + 0.1f
                };
                break;
            case 4006: // Stalker's Quiver
                snapshot = snapshot with
                {
                    MagicQuiver = true,
                    ArrowDamageAdditiveStack = snapshot.ArrowDamageAdditiveStack + 0.1f
                };
                break;
            case 4005: // Recon Scope
                snapshot = snapshot with
                {
                    RangedDamage = snapshot.RangedDamage + 0.1f,
                    RangedCrit = snapshot.RangedCrit + 10
                };
                break;
            case 1865: // Celestial Stone
            case 3110: // Celestial Shell
                skyStoneEffects = true;
                break;
            case 3212: // Shark Tooth Necklace
                snapshot = snapshot with { ArmorPenetration = snapshot.ArmorPenetration + 5 };
                break;
            default:
                return false;
        }

        return TryApplyAccessoryPrefix(prefix, ref snapshot);
    }

    private static bool TryApplyAccessoryPrefix(PrefixId prefix, ref VanillaPlayerCombatSnapshot snapshot)
    {
        if (prefix == VanillaPrefixIds.None)
            return true;

        switch (prefix.Value)
        {
            case 62: snapshot = snapshot with { Defense = snapshot.Defense + 1 }; return true;
            case 63: snapshot = snapshot with { Defense = snapshot.Defense + 2 }; return true;
            case 64: snapshot = snapshot with { Defense = snapshot.Defense + 3 }; return true;
            case 65: snapshot = snapshot with { Defense = snapshot.Defense + 4 }; return true;
            case 66: return true; // Arcane: mana only.
            case 67:
                snapshot = snapshot with { MeleeCrit = snapshot.MeleeCrit + 2, RangedCrit = snapshot.RangedCrit + 2, MagicCrit = snapshot.MagicCrit + 2 };
                return true;
            case 68:
                snapshot = snapshot with { MeleeCrit = snapshot.MeleeCrit + 4, RangedCrit = snapshot.RangedCrit + 4, MagicCrit = snapshot.MagicCrit + 4 };
                return true;
            case 69: return AddDamage(0.01f, ref snapshot);
            case 70: return AddDamage(0.02f, ref snapshot);
            case 71: return AddDamage(0.03f, ref snapshot);
            case 72: return AddDamage(0.04f, ref snapshot);
            case 73:
            case 74:
            case 75:
            case 76:
                return true; // movement-only prefixes
            case 77: snapshot = snapshot with { MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.01f }; return true;
            case 78: snapshot = snapshot with { MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.02f }; return true;
            case 79: snapshot = snapshot with { MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.03f }; return true;
            case 80: snapshot = snapshot with { MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.04f }; return true;
            default: return false;
        }
    }

    private static bool AddDamage(float amount, ref VanillaPlayerCombatSnapshot snapshot)
    {
        snapshot = snapshot with
        {
            MeleeDamage = snapshot.MeleeDamage + amount,
            RangedDamage = snapshot.RangedDamage + amount,
            MagicDamage = snapshot.MagicDamage + amount
        };
        return true;
    }
}
