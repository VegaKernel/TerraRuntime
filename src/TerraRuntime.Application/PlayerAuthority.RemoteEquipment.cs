using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool TryCaptureProvenRemoteEquipment(
        ReadOnlySpan<PlayerEquipmentCommitRequest> equipment, PlayerAppearanceCommitRequest? appearance,
        out VanillaPlayerCombatSnapshot combat)
    {
        combat = default;
        bool inherited = false;
        Span<PlayerEquipmentCommitRequest> vanityPiece = stackalloc PlayerEquipmentCommitRequest[1];
        // Pin the whole-phase family independently of future combat-catalog additions.
        foreach (var item in equipment)
        {
            if (item.Stack <= 0) continue;
            bool inactiveArmor = false;
            int index = item.SlotId - VanillaPlayerItemSlotCatalog.ArmorStart;
            if ((uint)index >= VanillaPlayerItemSlotCatalog.FunctionalArmorCount)
            {
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
            bool defensiveAccessory = index is >= 3 and <= 9 && item.ItemNetId is 156 or 3212;
            if (defensiveAccessory)
            {
                // Arcane changes derived mana maximum; Ankh adds immunity writers. Neither
                // can be admitted merely because the combat projection otherwise looks neutral.
                if (item.PrefixId.Value is not (0 or 62 or 63 or 64 or 65 or 67 or 68)) return false;
            }
            else if (item.PrefixId != default || (!IsProvenNeutralMetal(item.ItemNetId) && !sharingBlocker)) return false;
        }
        if (inherited && combatEquipmentLocalVanityArmor != VanillaPlayerLocalVanityArmor1458.Empty) return false;

        // The common catalog owns piece identity, correct slots, prefixes and complete-set
        // defense. Only the independently captured defense/crit/penetration/knockback writers
        // may differ; a future combat-catalog addition cannot expand this player phase.
        bool? extraAccessory = appearance is { } owned
            ? (owned.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag) != 0 : null;
        var context = new VanillaPlayerCombatEquipmentContext(extraAccessory, combatEquipmentExpertMode, combatEquipmentMasterMode)
        { LocalVanityArmor = combatEquipmentLocalVanityArmor };
        if (!VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in context, out combat)) return false;
        return combat with
        {
            Defense = 0, ArmorPenetration = 0, NoKnockback = false,
            MeleeCrit = 4, RangedCrit = 4, MagicCrit = 4
        } == VanillaPlayerCombatSnapshot.Baseline;
    }

    private static bool IsProvenNeutralMetal(int type) => type is
        76 or 77 or 78 or 79 or 80 or 81 or 82 or 83 or 89 or 90 or 91 or 92 or
        687 or 688 or 689 or 690 or 691 or 692 or 693 or 694 or 695 or 696 or 697 or 698 or 954 or 955;
}
