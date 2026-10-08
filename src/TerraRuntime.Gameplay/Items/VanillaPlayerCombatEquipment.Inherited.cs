using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Items;

/// <summary>The three actual Main.LocalPlayer vanity armor identities used by source CanShareArmor.</summary>
public readonly record struct VanillaPlayerLocalVanityArmor1458(ItemTypeId Head, ItemTypeId Body, ItemTypeId Legs)
{
    public static VanillaPlayerLocalVanityArmor1458 Empty => default;
}

public static partial class VanillaPlayerCombatEquipmentCatalog
{
    private static bool HasEffectiveCandidate(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment, int index)
    {
        if (FindEquipment(equipment, VanillaPlayerItemSlotCatalog.ArmorStart + index).Stack > 0) return true;
        return TryFindFirstFavorite(equipment, index, out _);
    }

    private static PlayerEquipmentCommitRequest FindEquipment(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment, int slot)
    {
        foreach (var item in equipment)
            if (item.SlotId == slot && item.Stack > 0 && item.ItemNetId > 0) return item;
        return default;
    }

    private static bool TryFindFirstFavorite(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment, int index,
        out PlayerEquipmentCommitRequest favorite)
    {
        for (int loadout = 0; loadout < VanillaPlayerItemSlotCatalog.LoadoutCount; loadout++)
        {
            favorite = FindEquipment(equipment, VanillaPlayerItemSlotCatalog.LoadoutArmorStart +
                loadout * VanillaPlayerItemSlotCatalog.LoadoutStride + index);
            if (favorite.Stack > 0 && (favorite.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) return true;
        }
        favorite = default;
        return false;
    }

    private static bool TrySelectEffectiveEquipment(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        in VanillaPlayerCombatEquipmentContext context, int index, out PlayerEquipmentCommitRequest selected)
    {
        selected = FindEquipment(equipment, VanillaPlayerItemSlotCatalog.ArmorStart + index);
        if (selected.Stack > 0 || !TryFindFirstFavorite(equipment, index, out var candidate)) return true;
        if (!TryGetSharingMetadata(candidate.ItemNetId, out var metadata)) return false;
        if (!TryCanShareEquipment(equipment, in context, index, candidate.ItemNetId, in metadata, out bool compatible)) return false;
        // The first incompatible favorite ends the search. A later loadout must not replace it.
        if (compatible) selected = candidate with { SlotId = checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + index)) };
        return true;
    }

    private static bool TryCanShareEquipment(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        in VanillaPlayerCombatEquipmentContext context, int index, int type, in SharingMetadata metadata, out bool compatible)
    {
        compatible = false;
        if (index < 3)
        {
            if (!metadata.FitsArmor(index)) return true;
            if (context.LocalVanityArmor is not { } local) return false;
            // The source armor branch reads Main.LocalPlayer, unlike the accessory branch below.
            compatible = type != local.Head.Value && type != local.Body.Value && type != local.Legs.Value;
            return true;
        }
        if (!metadata.Accessory) return true;

        for (int slot = 3; slot < VanillaPlayerItemSlotCatalog.FunctionalArmorCount; slot++)
        {
            var active = FindEquipment(equipment, VanillaPlayerItemSlotCatalog.ArmorStart + slot);
            if (active.Stack <= 0) continue;
            if (!TryCanEquipTogether(type, in metadata, active.ItemNetId, out bool together)) return false;
            if (!together) return true;
        }
        for (int slot = 13; slot < VanillaPlayerItemSlotCatalog.ArmorCount; slot++)
        {
            var vanity = FindEquipment(equipment, VanillaPlayerItemSlotCatalog.ArmorStart + slot);
            if (vanity.Stack <= 0) continue;
            if (vanity.ItemNetId == type) return true;
            if (metadata.Incompatibility < 0) continue;
            if (!TryGetSharingMetadata(vanity.ItemNetId, out var other)) return false;
            if (other.Incompatibility == metadata.Incompatibility) return true;
        }
        // Empty earlier slots may themselves inherit their first favorite. These checks do not
        // recursively choose a replacement when that favorite conflicts or is unusable.
        for (int slot = index - 1; slot >= 3; slot--)
        {
            if (FindEquipment(equipment, VanillaPlayerItemSlotCatalog.ArmorStart + slot).Stack > 0 ||
                !TryFindFirstFavorite(equipment, slot, out var earlier)) continue;
            if (!TryCanEquipTogether(type, in metadata, earlier.ItemNetId, out bool together)) return false;
            if (!together) return true;
        }
        compatible = true;
        return true;
    }

    private static bool TryCanEquipTogether(int type, in SharingMetadata metadata, int otherType, out bool together)
    {
        together = false;
        if (type == otherType) return true;
        // Irrelevant unknown neighbors do not require metadata. Only a selected wing or
        // incompatibility group can depend on an otherwise unsupported neighbor's defaults.
        if (metadata.Wing > 0 || metadata.Incompatibility >= 0)
        {
            if (!TryGetSharingMetadata(otherType, out var other)) return false;
            if (metadata.Wing > 0 && other.Wing > 0 ||
                metadata.Incompatibility >= 0 && metadata.Incompatibility == other.Incompatibility) return true;
        }
        together = true;
        return true;
    }

    private readonly record struct SharingMetadata(int ArmorIndex, bool Accessory = false, int Wing = -1, int Incompatibility = -1)
    {
        public bool FitsArmor(int index) => ArmorIndex == index;
    }

    private static bool TryGetSharingMetadata(int type, out SharingMetadata metadata)
    {
        // Independently executed original Item.SetDefaults and ItemID.Sets. These identities
        // describe selection compatibility only; they do not admit new combat benefits.
        metadata = type switch
        {
            89 or 90 or 91 or 92 or 687 or 690 or 693 or 696 or 954 or 955 or
                1546 or 1547 or 2199 or 2757 or 2763 => new(0),
            80 or 81 or 82 or 83 or 688 or 691 or 694 or 697 or 1549 or 2200 or
                2201 or 2758 or 2764 => new(1),
            76 or 77 or 78 or 79 or 689 or 692 or 695 or 698 or 1550 or 2202 or
                2759 or 2765 => new(2),
            49 or 156 or 211 or 489 or 490 or 491 or 1301 or 1321 or 1613 or
                1858 or 3110 or 3212 or 4989 or 5000 or 5107 => new(-1, true),
            2609 => new(-1, true, 26),
            3580 => new(-1, true, 33),
            6168 or 6169 or 6193 or 6194 => new(-1, true, -1, 6168),
            3508 => new(-1),
            _ => new(-2)
        };
        return metadata.ArmorIndex != -2;
    }
}
