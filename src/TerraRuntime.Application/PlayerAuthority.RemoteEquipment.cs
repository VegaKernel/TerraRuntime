using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool TryCaptureProvenRemoteEquipment(
        ReadOnlySpan<PlayerEquipmentCommitRequest> equipment, PlayerAppearanceCommitRequest? appearance,
        out VanillaPlayerCombatSnapshot combat, out int manaMaximumBonus)
    {
        combat = default;
        manaMaximumBonus = 0;
        bool inherited = false;
        Span<PlayerEquipmentCommitRequest> vanityPiece = stackalloc PlayerEquipmentCommitRequest[1];
        // Pin the whole-phase family independently of future combat-catalog additions.
        foreach (var item in equipment)
        {
            if (item.Stack <= 0) continue;
            // Bank4 is mirrored in the transfer profile, but it is not equipped armor.
            // These captured consumables write neither source information nor mechanical
            // accessory fields. Other bank items retain the existing whole-phase refusal:
            // source void-eligible 6190 writes oldStyleParkour, which this phase does not own.
            if (item.SlotId is >= VanillaPlayerItemSlotCatalog.Bank4Start and
                < VanillaPlayerItemSlotCatalog.Bank4EndExclusive &&
                VanillaSelectedConsumableCatalog1458.TryGet(new ItemTypeId((ushort)item.ItemNetId), out var stored) &&
                stored.BuffType > 0) continue;
            bool inactiveArmor = false;
            int index = item.SlotId - VanillaPlayerItemSlotCatalog.ArmorStart;
            if ((uint)index >= VanillaPlayerItemSlotCatalog.FunctionalArmorCount)
            {
                // Original source records for these known None vanity accessories prove
                // no functional grants, but they can block inherited matching favorites.
                if (index is >= 13 and <= 19 && item.ItemNetId is 156 or 3212 or 489 or 490 or 491 or 2998 or 935 or 1301 or 1858 or 854 or 3033 or 3034 or 3035 or 1321 &&
                    item.PrefixId == default) continue;
                if (index is >= 10 and <= 12)
                {
                    // Cosmetic armor has no functional grants, but its source slot identity
                    // still needs to belong to the independently captured metal family.
                    vanityPiece[0] = item with { SlotId = checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + index - 10)) };
                    if (!VanillaPlayerCombatEquipmentCatalog.TryBuild(vanityPiece, out _)) return false;
                }
                else
                {
                    int loadout = item.SlotId - VanillaPlayerItemSlotCatalog.LoadoutArmorStart;
                    if (loadout < 0 || loadout >= VanillaPlayerItemSlotCatalog.LoadoutCount * VanillaPlayerItemSlotCatalog.LoadoutStride ||
                        loadout % VanillaPlayerItemSlotCatalog.LoadoutStride >= VanillaPlayerItemSlotCatalog.FunctionalArmorCount) return false;
                    index = loadout % VanillaPlayerItemSlotCatalog.LoadoutStride;
                    inherited = true;
                    inactiveArmor = true;
                }
            }
            // Source CanShareArmor rejects this known weapon in every armor position
            // before armor grants; it may block a later favorite but grants no armor benefits.
            bool sharingBlocker = inactiveArmor && item.ItemNetId == VanillaItemIds.CopperBroadsword.Value;
            bool provenAccessory = index is >= 3 and <= 9 && item.ItemNetId is
                156 or 3212 or 489 or 490 or 491 or 2998 or 935 or 1301 or 1858 or 854 or 3033 or 3034 or 3035 or 1321;
            if (index is >= 3 and <= 9 && item.ItemNetId is 555 or 854 or 3033 or 3034 or 3035)
            {
                if (item.PrefixId != default) return false;
            }
            else if (provenAccessory)
            {
                if (item.PrefixId.Value is >= 77 and <= 80 && item.ItemNetId is not
                    (489 or 490 or 491 or 2998 or 935 or 1301 or 1858 or 854 or 3033 or 3034 or 3035 or 1321)) return false;
                // Arcane is staged through the common effective-slot mana owner; Ankh
                // immunity writers still require a separate buff-phase owner.
                if (item.PrefixId.Value is not (0 or 62 or 63 or 64 or 65 or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 77 or 78 or 79 or 80)) return false;
            }
            else if (item.PrefixId != default || (!IsProvenNeutralMetal(item.ItemNetId) &&
                !IsProvenAmmoConservationArmor(item.ItemNetId, index) && !sharingBlocker)) return false;
        }
        if (inherited && combatEquipmentLocalVanityArmor != VanillaPlayerLocalVanityArmor1458.Empty) return false;

        // The common catalog owns piece identity, correct slots, prefixes and complete-set
        // defense. Only the independently captured damage/defense/crit/penetration/knockback writers
        // may differ; a future combat-catalog addition cannot expand this player phase.
        bool? extraAccessory = appearance is { } owned
            ? (owned.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag) != 0 : null;
        var context = new VanillaPlayerCombatEquipmentContext(extraAccessory, combatEquipmentExpertMode, combatEquipmentMasterMode)
        { LocalVanityArmor = combatEquipmentLocalVanityArmor };
        if (!VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in context, out combat, out manaMaximumBonus)) return false;
        return combat with
        {
            Defense = 0, ArmorPenetration = 0, NoKnockback = false,
            MeleeCrit = 4, RangedCrit = 4, MagicCrit = 4, ManaFlower = false, ManaCostMultiplier = 1f,
            DiscountEquipped = false, GoldRing = false, HasLuckyCoin = false, HasLuckLuckyCoin = false,
            MeleeDamage = 1f, RangedDamage = 1f, MagicDamage = 1f, MinionDamage = 1f, MeleeAttackSpeed = 1f,
            MagicQuiver = false, ArrowDamageAdditiveStack = 0f,
            HuntressAmmoCost90 = false, ChloroAmmoCost80 = false, AmmoCost80 = false, AmmoCost75 = false
        } == VanillaPlayerCombatSnapshot.Baseline;
    }

    // Source-certified isolated armor grants; the shared catalog still validates effective
    // slot identity, complete sets and prefixes before this phase projection.
    private static bool IsProvenAmmoConservationArmor(int type, int armorIndex) =>
        (armorIndex, type) is (0, 1002) or (1, 3804) or (1, 4983) or (1, 3878) or
            (1, 1549) or (1, 2758);

    private static bool IsProvenNeutralMetal(int type) => type is
        76 or 77 or 78 or 79 or 80 or 81 or 82 or 83 or 89 or 90 or 91 or 92 or
        687 or 688 or 689 or 690 or 691 or 692 or 693 or 694 or 695 or 696 or 697 or 698 or 954 or 955;
}
