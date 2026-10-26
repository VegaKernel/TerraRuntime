using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Items;

public static partial class VanillaPlayerCombatEquipmentCatalog
{
    // Player.GrantArmorBenefits (1458) writes flowerBoots for both items.
    // Reuse source slot usability and inherited-favorite selection; physical
    // presence in a backpack, vanity slot or unused loadout is not this effect.
    public static bool TryCaptureFlowerBoots(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        in VanillaPlayerCombatEquipmentContext context, out bool enabled)
    {
        enabled = false;
        for (int index = 0; index < VanillaPlayerItemSlotCatalog.FunctionalArmorCount; index++)
        {
            if (!HasEffectiveCandidate(equipment, index)) continue;
            if (!context.TryIsSlotUsable(index, out bool usable)) return false;
            if (!usable) continue;
            if (!TrySelectEffectiveEquipment(equipment, in context, index, out var selected)) return false;
            if (selected.Stack <= 0) continue;
            if (!selected.TryGetCanonicalItemType(out var type)) return false;
            if (type.Value is 3017 or 3993) enabled = true;
        }
        return true;
    }
}
