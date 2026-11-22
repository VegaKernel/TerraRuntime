using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;
internal sealed partial class PlayerAuthority
{
    private bool TryCaptureProvenRemoteEquipment(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        PlayerAppearanceCommitRequest? appearance,
        out VanillaPlayerCombatSnapshot combat,
        out int manaMaximumBonus,
        bool voidStorageOpen, bool allowAmmoCycling = false,
        bool stationaryToolProfile = false)
    {
        combat = default;
        manaMaximumBonus = 0;
        bool inherited = false;
        bool Reject(int reason, in PlayerEquipmentCommitRequest item)
        {
            RuntimeTileManipulationTelemetry.Shared.RecordRemoteEquipmentFailure(reason, in item);
            return false;
        }
        Span<PlayerEquipmentCommitRequest> vanityPiece = stackalloc PlayerEquipmentCommitRequest[1];
        // Pin the whole-phase family independently of future combat-catalog additions.
        foreach (var item in equipment)
        {
            if (item.Stack <= 0)
                continue;
            // Stored Void items never grant armor benefits. Only an open bag
            // captured from ordinary slots 0..57 activates source information
            // and mechanical writers; unknown identities remain unsupported.
            if (item.SlotId is >= VanillaPlayerItemSlotCatalog.Bank4Start and < VanillaPlayerItemSlotCatalog.Bank4EndExclusive)
            {
                if (!VanillaVoidStorageCatalog1458.TryGet(new ItemTypeId((ushort)item.ItemNetId), out var storedFacts))
                    return Reject(1, in item);
                if (!voidStorageOpen || storedFacts.IsPhaseNeutral)
                    continue;
                return Reject(2, in item);
            }

            // Player.UpdateDyes (1.4.5.8) reads these observed dye slots only
            // into visual shader fields. Their exact identities do not grant
            // combat, tool, movement or storage effects to this player phase.
            bool knownGoodDye = item.SlotId switch
            {
                79 => item.ItemNetId == 2872,
                80 or 81 or 82 or 84 or 85 or 86 or 88 or 96 => item.ItemNetId == 3557,
                83 => item.ItemNetId == 3529,
                87 => item.ItemNetId == 1030,
                98 => item.ItemNetId == 2883,
                _ => false
            };
            if (knownGoodDye && item.PrefixId == default) continue;

            // Item.SetDefaults pins these GOOD misc identities to cart11,
            // mount23 and hook projectile165. Mere storage in misc slots does
            // not activate them; active mount and grappling state have separate
            // player-phase admission and physics owners.
            if (item.PrefixId == default && (item.SlotId, item.ItemNetId) is
                (91, 3353) or (92, 4444) or (93, 939))
                continue;

            bool inactiveArmor = false;
            int index = item.SlotId - VanillaPlayerItemSlotCatalog.ArmorStart;
            if ((uint)index >= VanillaPlayerItemSlotCatalog.FunctionalArmorCount)
            {
                // Player.UpdateVisibleAccessories (1.4.5.8) sets only the
                // observed horn/darkness display flags for these social slots.
                if (item.PrefixId == default && (index, item.ItemNetId) is
                    (14, 4563) or (16, 3581))
                    continue;
                // ApplyEquipVanity exposes wing41 for the observed social item;
                // stationary tool phases never enter WingAirVisuals/WingMovement.
                if (stationaryToolProfile && item.PrefixId == default &&
                    (index, item.ItemNetId) is (13, 4746))
                    continue;
                // Player.ApplyEquipVanity (1.4.5.8) has no simulated writer for
                // these exact GOOD social accessories; their functional effects
                // are confined to slots 3..9. The social wing remains gated.
                if (item.PrefixId == default && (index, item.ItemNetId) is
                    (15, 4771) or (17, 1921) or (18, 1323) or (19, 554))
                    continue;
                // Original source records for these known None vanity accessories prove
                // no functional grants, but they can block inherited matching favorites.
                if (index is >= 13 and <= 19 &&
                    (item.ItemNetId is 885 or 886 or 887 or 888 or 889 or 890 or 891 or 892 or 893 or 3781 or 901 or 902 or 903 or 904 or 5354 or 1612 or 156 or 3212 or 489 or 490 or 491 or 2998 or 935 or 1301 or 1858 or 854 or 3033 or 3034 or 3035 or 1321 &&
                     item.PrefixId == default ||
                     item.ItemNetId is 6168 or 6169 or 6193 or 6194 &&
                     item.PrefixId.Value is 0 or >= 62 and <= 80))
                    continue;
                if (index is >= 10 and <= 12)
                {
                    // Player.UpdateEquips (1.4.5.8) does not grant armor benefits from
                    // social head/body/leg slots. Item.SetDefaults pins these three
                    // GOOD pieces to the matching visual slot; body3267's defense=6
                    // must not leak through the functional armor catalog.
                    if (item.PrefixId == default && (index, item.ItemNetId) is
                        (10, 4955) or (11, 3267) or (12, 844))
                        continue;
                    // Cosmetic armor has no functional grants, but its source slot identity
                    // still needs to belong to the independently captured metal family.
                    vanityPiece[0] = item with
                    {
                        SlotId = checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + index - 10))
                    };
                    if (!VanillaPlayerCombatEquipmentCatalog.TryBuild(vanityPiece, out _))
                        return Reject(3, in item);
                }
                else
                {
                    int loadout = item.SlotId - VanillaPlayerItemSlotCatalog.LoadoutArmorStart;
                    if (loadout < 0 ||
                        loadout >= VanillaPlayerItemSlotCatalog.LoadoutCount * VanillaPlayerItemSlotCatalog.LoadoutStride ||
                        loadout % VanillaPlayerItemSlotCatalog.LoadoutStride >= VanillaPlayerItemSlotCatalog.FunctionalArmorCount)
                        return Reject(4, in item);
                    index = loadout % VanillaPlayerItemSlotCatalog.LoadoutStride;
                    inherited = true;
                    inactiveArmor = true;
                }
            }

            // Source CanShareArmor rejects this known weapon in every armor position
            // before armor grants; it may block a later favorite but grants no armor benefits.
            bool sharingBlocker = inactiveArmor && item.ItemNetId == VanillaItemIds.CopperBroadsword.Value;
            // Source 1.4.5.8 functional effects are independently projected by
            // the combat and builder catalogs. Motion-dependent writers are
            // admitted only by the grounded, motionless tool profile below.
            if (stationaryToolProfile &&
                (index, item.ItemNetId, item.PrefixId.Value) is
                (3, 4954, 74) or (4, 2423, 0) or (5, 216, 75) or
                (6, 4989, 0) or (7, 3061, 0) or (8, 5000, 0) or (9, 407, 0))
                continue;
            bool cyclingAccessory = allowAmmoCycling && index is >= 3 and <= 9 && item.ItemNetId is 6168 or 6169 or 6193 or 6194;
            bool provenAccessory = index is >= 3 and <= 9 &&
                item.ItemNetId is 885 or 886 or 887 or 888 or 889 or 890 or 891 or 892 or 893 or 3781 or 901 or 902 or 903 or 904 or 5354 or 1612 or 156 or 3212 or 489 or 490 or 491 or 2998 or 935 or 1301 or 1858 or 854 or 3033 or 3034 or 3035 or 1321;
            if (index is >= 3 and <= 9 && item.ItemNetId is 555 or 854 or 3033 or 3034 or 3035)
            {
                if (item.PrefixId != default)
                    return Reject(5, in item);
            }
            else if (cyclingAccessory)
            {
                if (item.PrefixId.Value is not (0 or >= 62 and <= 80)) return Reject(6, in item);
            }
            else if (provenAccessory)
            {
                if (item.PrefixId.Value is >= 77 and <= 80 && item.ItemNetId is not (885 or 886 or 887 or 888 or 889 or 890 or 891 or 892 or 893 or 3781 or 901 or 902 or 903 or 904 or 5354 or 1612 or 489 or 490 or 491 or 2998 or 935 or 1301 or 1858 or 854 or 3033 or 3034 or 3035 or 1321))
                    return Reject(7, in item);
                // Arcane is staged through the common effective-slot mana owner; Ankh
                // immunity writers still require a separate buff-phase owner.
                if (item.PrefixId.Value is not (0 or 62 or 63 or 64 or 65 or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 77 or 78 or 79 or 80))
                    return Reject(8, in item);
            }
            else if (item.PrefixId != default ||
                (!IsProvenNeutralMetal(item.ItemNetId) &&
                !IsProvenAmmoConservationArmor(item.ItemNetId,
                index) &&
                !(index == 1 &&
                VanillaGemStaff1458.TryGetRobe(new ItemTypeId((ushort)item.ItemNetId),
                out _)) &&
                !(index == 0 && item.ItemNetId == VanillaDeerclopsItemIds.Eyebrella.Value) &&
                !sharingBlocker))
                return Reject(9, in item);
        }

        if (inherited && combatEquipmentLocalVanityArmor != VanillaPlayerLocalVanityArmor1458.Empty)
        {
            RuntimeTileManipulationTelemetry.Shared.RecordRemoteEquipmentFailure(10);
            return false;
        }
        // The common catalog owns piece identity, correct slots, prefixes and complete-set
        // defense. Only the independently captured damage/defense/crit/penetration/knockback writers
        // may differ; a future combat-catalog addition cannot expand this player phase.
        bool? extraAccessory = appearance is { } owned ? (owned.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag) != 0 : null;
        var context = new VanillaPlayerCombatEquipmentContext(extraAccessory, combatEquipmentExpertMode, combatEquipmentMasterMode)
        {
            LocalVanityArmor = combatEquipmentLocalVanityArmor,
            EnableAmmoCyclingAccessories = allowAmmoCycling
        };
        if (!VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in context, out combat, out manaMaximumBonus))
        {
            RuntimeTileManipulationTelemetry.Shared.RecordRemoteEquipmentFailure(11);
            return false;
        }
        return combat with
        {
            BuffImmunity = VanillaPlayerBuffImmunity1458.None,
            Defense = 0,
            ArmorPenetration = 0,
            NoKnockback = false,
            MeleeCrit = 4,
            RangedCrit = 4,
            MagicCrit = 4,
            ManaFlower = false,
            ManaCostMultiplier = 1f,
            DiscountEquipped = false,
            GoldRing = false,
            HasLuckyCoin = false,
            HasLuckLuckyCoin = false,
            MeleeDamage = 1f,
            RangedDamage = 1f,
            MagicDamage = 1f,
            MinionDamage = 1f,
            MeleeAttackSpeed = 1f,
            MagicQuiver = false,
            ArrowDamageAdditiveStack = 0f,
            HuntressAmmoCost90 = false,
            ChloroAmmoCost80 = false,
            AmmoCost80 = false,
            AmmoCost75 = false,
            AmmoCyclingMode = AmmoCyclingMode1458.None,
            AmmoCyclerDamage = false,
            MoveSpeed = 1f
        }

        == VanillaPlayerCombatSnapshot.Baseline;
    }


    private bool TryBuildSourceOrderedBuffCombat(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        PlayerAppearanceCommitRequest? appearance,
        ReadOnlySpan<BuffTypeId> buffs,
        out VanillaPlayerCombatSnapshot combat, bool allowAmmoCycling = false)
    {
        // The caller has already passed the unchanged whole-phase equipment admission.
        // For food and Weak, replay UpdateBuffs before UpdateEquips
        // rather than commuting Single additions over equipment/prefix grants.
        combat = default;
        var baseline = VanillaPlayerCombatSnapshot.Baseline;
        if (!VanillaPlayerCombatBuffs1458.TryApplyBeforeEquipment(in baseline, buffs, out var beforeEquipment))
            return false;
        bool? extraAccessory = appearance is { } owned ? (owned.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag) != 0 : null;
        var context = new VanillaPlayerCombatEquipmentContext(extraAccessory, combatEquipmentExpertMode, combatEquipmentMasterMode)
        {
            LocalVanityArmor = combatEquipmentLocalVanityArmor,
            EnableAmmoCyclingAccessories = allowAmmoCycling,
            SourceBuffDefenseBeforeEquipment = true
        };
        return VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in context, in beforeEquipment, out combat, out _);
    }

    // Source-certified isolated armor grants; the shared catalog still validates effective
    // slot identity, complete sets and prefixes before this phase projection.
    private static bool IsProvenAmmoConservationArmor(int type, int armorIndex) => (armorIndex, type)is (0, 1002) or (1, 3804) or (1, 4983) or (1, 3878) or (1, 1549) or (1, 2758);
    private static bool IsProvenNeutralMetal(int type) => type is 76 or 77 or 78 or 79 or 80 or 81 or 82 or 83 or 89 or 90 or 91 or 92 or 687 or 688 or 689 or 690 or 691 or 692 or 693 or 694 or 695 or 696 or 697 or 698 or 954 or 955;
}
