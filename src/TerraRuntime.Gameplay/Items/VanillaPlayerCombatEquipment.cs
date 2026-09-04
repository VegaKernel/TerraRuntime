using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Source-backed subset of Player.ResetEffects + UpdateEquips + GrantArmorBenefits + GrantPrefixBenefits
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
    float ArrowDamageAdditiveStack,
    float BulletDamage,
    int MeleeCrit,
    int RangedCrit,
    int MagicCrit,
    float MeleeAttackSpeed,
    int ArmorPenetration,
    int MeleeArmorPenetration,
    bool NoKnockback,
    bool MagicQuiver)
{
    public static VanillaPlayerCombatSnapshot Baseline => new(
        Defense: 0,
        Endurance: 0f,
        MeleeDamage: 1f,
        RangedDamage: 1f,
        MagicDamage: 1f,
        RangedMultDamage: 1f,
        ArrowDamage: 1f,
        ArrowDamageAdditiveStack: 0f,
        BulletDamage: 1f,
        MeleeCrit: VanillaItemCombatCatalog.VanillaBaseMeleeCrit,
        RangedCrit: 4,
        MagicCrit: 4,
        MeleeAttackSpeed: 1f,
        ArmorPenetration: 0,
        MeleeArmorPenetration: 0,
        NoKnockback: false,
        MagicQuiver: false);

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
            if (armorIndex == 0)
            {
                if (type != VanillaItemIds.CopperHelmet || request.PrefixId != VanillaPrefixIds.None)
                    return false;
                head = type;
                snapshot = snapshot with { Defense = snapshot.Defense + 1 };
                continue;
            }
            if (armorIndex == 1)
            {
                if (type != VanillaItemIds.CopperChainmail || request.PrefixId != VanillaPrefixIds.None)
                    return false;
                body = type;
                snapshot = snapshot with { Defense = snapshot.Defense + 2 };
                continue;
            }
            if (armorIndex == 2)
            {
                if (type != VanillaItemIds.CopperGreaves || request.PrefixId != VanillaPrefixIds.None)
                    return false;
                legs = type;
                snapshot = snapshot with { Defense = snapshot.Defense + 1 };
                continue;
            }

            if (!TryApplyAccessory(type, request.PrefixId, ref snapshot))
                return false;
        }

        // ArmorSetBonuses.Initialize: MetalTier1.Set(89, 80, 76) => +2 defense.
        if (head == VanillaItemIds.CopperHelmet &&
            body == VanillaItemIds.CopperChainmail &&
            legs == VanillaItemIds.CopperGreaves)
        {
            snapshot = snapshot with { Defense = snapshot.Defense + 2 };
        }

        return true;
    }

    private static bool TryApplyAccessory(
        ItemTypeId type,
        PrefixId prefix,
        ref VanillaPlayerCombatSnapshot snapshot)
    {
        switch (type.Value)
        {
            case 156: // Cobalt Shield
                snapshot = snapshot with { Defense = snapshot.Defense + 1, NoKnockback = true };
                break;
            case 489: // Sorcerer Emblem: Player.UpdateEquips adds 15% magic damage.
                snapshot = snapshot with { MagicDamage = snapshot.MagicDamage + 0.15f };
                break;
            case 490: // Warrior Emblem
                snapshot = snapshot with { MeleeDamage = snapshot.MeleeDamage + 0.15f };
                break;
            case 491: // Ranger Emblem
                snapshot = snapshot with { RangedDamage = snapshot.RangedDamage + 0.15f };
                break;
            case 1321: // Magic Quiver
                snapshot = snapshot with
                {
                    MagicQuiver = true,
                    ArrowDamageAdditiveStack = snapshot.ArrowDamageAdditiveStack + 0.1f
                };
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
