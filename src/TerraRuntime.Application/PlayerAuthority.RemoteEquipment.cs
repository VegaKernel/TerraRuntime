using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool TryCaptureNeutralRemoteEquipment(
        ReadOnlySpan<PlayerEquipmentCommitRequest> equipment, out VanillaPlayerCombatSnapshot combat)
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
            if ((uint)index >= 3)
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
                        loadout % VanillaPlayerItemSlotCatalog.LoadoutStride >= 3) return false;
                    index = loadout % VanillaPlayerItemSlotCatalog.LoadoutStride;
                    inherited = true;
                    inactiveArmor = true;
                }
            }
            // Source CanShareArmor rejects this known weapon in every armor position
            // before armor grants; it may block a later favorite but grants no armor benefits.
            bool sharingBlocker = inactiveArmor && item.ItemNetId == VanillaItemIds.CopperBroadsword.Value;
            if (item.PrefixId != default || (!IsProvenNeutralMetal(item.ItemNetId) && !sharingBlocker)) return false;
        }
        if (inherited && combatEquipmentLocalVanityArmor != VanillaPlayerLocalVanityArmor1458.Empty) return false;

        // The common catalog owns piece identity, correct slots, prefixes and complete-set
        // defense. Do not treat a queryable item or an unrepresented modifier as neutral.
        var context = new VanillaPlayerCombatEquipmentContext(null, combatEquipmentExpertMode, combatEquipmentMasterMode)
        { LocalVanityArmor = combatEquipmentLocalVanityArmor };
        if (!VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in context, out combat)) return false;
        return combat with { Defense = 0 } == VanillaPlayerCombatSnapshot.Baseline;
    }

    private static bool IsProvenNeutralMetal(int type) => type is
        76 or 77 or 78 or 79 or 80 or 81 or 82 or 83 or 89 or 90 or 91 or 92 or
        687 or 688 or 689 or 690 or 691 or 692 or 693 or 694 or 695 or 696 or 697 or 698 or 954 or 955;
}
