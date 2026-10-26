using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public enum AmmoCyclingMode1458 : byte
{
    None,
    AmmoSlots,
    FullInventory,
    Random
}

/// <summary>Player-owned persistent state; neither field is supplied by an ammo report.</summary>
public readonly record struct AmmoCyclingState1458(int Offset, int Cooldown);

/// <summary>Detached source metadata/stock projection for one of the 58 ordinary inventory slots.</summary>
public readonly record struct AmmoCyclingSlot1458(int AmmoFamily, int Stack);

/// <summary>
/// Detached Player 1.4.5.8 ammo selector and its persistent clock transition.
/// This helper does not grant equipment, paid stock, a local phase or projectile authority.
/// </summary>
public static class VanillaAmmoCycling1458
{
    private const int CoinAmmoFamily = 71;
    private const int InventorySlotCount = VanillaPlayerItemSlotCatalog.InventoryMouseItem;
    private const int SourceCooldownTail = 120;

    // Source iteration counts occurrences, not distinct slots. Coin slots occur
    // twice in the default order; deduplicating them changes cycling selection.
    private static readonly int[] DefaultOrder =
        Enumerable.Range(50, 4).Concat(Enumerable.Range(54, 4)).Concat(Enumerable.Range(0, 54)).ToArray();
    private static readonly int[] CoinsOrder = [50, 51, 52, 53];
    private static readonly int[] AmmoOrder = [54, 55, 56, 57];

    public static bool TryGetAccessoryMode(ItemTypeId type, out AmmoCyclingMode1458 mode)
    {
        mode = type.Value switch
        {
            6168 => AmmoCyclingMode1458.AmmoSlots,
            6169 => AmmoCyclingMode1458.FullInventory,
            6193 => AmmoCyclingMode1458.Random,
            _ => AmmoCyclingMode1458.None
        };
        return type.Value is 6168 or 6169 or 6193 or 6194;
    }

    /// <summary>
    /// The caller supplies already-selected usable functional accessory slots in
    /// source order. The last cycler sets mode; every cycler sets the same flag.
    /// UpdateEquips adds ranged damage once after that loop, not once per item.
    /// </summary>
    public static void ProjectEffectiveAccessories(ReadOnlySpan<ItemTypeId> effectiveAccessories,
        out AmmoCyclingMode1458 mode, out bool damageBonus)
    {
        mode = AmmoCyclingMode1458.None;
        damageBonus = false;
        foreach (var type in effectiveAccessories)
        {
            if (!TryGetAccessoryMode(type, out var offered)) continue;
            mode = offered;
            damageBonus = true;
        }
    }

    /// <summary>One source UpdateEquips addition after functional accessories.</summary>
    public static VanillaPlayerCombatSnapshot ApplyEquipmentDamageBonus(
        in VanillaPlayerCombatSnapshot before, bool damageBonus) =>
        damageBonus ? before with { RangedDamage = before.RangedDamage + 0.08f } : before;

    /// <summary>Update_AmmoCycler runs in the source-local pre-death Update branch.</summary>
    public static AmmoCyclingState1458 AdvanceLocalUpdate(in AmmoCyclingState1458 before)
    {
        if (before.Cooldown <= 0) return before;
        int remaining = before.Cooldown - 1;
        return new(remaining == 0 ? 0 : before.Offset, remaining);
    }

    /// <summary>
    /// HasAmmo is an existence query before the use attempt. It is not the
    /// cycling selector: a wrapped negative source offset can find no selected
    /// item even though HasAmmo allowed the source animation to start.
    /// </summary>
    public static bool TryHasAmmo(int ammoFamily, ReadOnlySpan<AmmoCyclingSlot1458> inventory,
        out bool hasAmmo)
    {
        hasAmmo = false;
        if (ammoFamily <= 0 || inventory.Length != InventorySlotCount) return false;
        foreach (var item in inventory)
        {
            if (item.AmmoFamily < 0) return false;
            hasAmmo |= item.AmmoFamily == ammoFamily && item.Stack > 0;
        }
        return true;
    }

    public static bool TrySelect(int weaponType, int ammoFamily, AmmoCyclingMode1458 mode,
        in AmmoCyclingState1458 state, ReadOnlySpan<AmmoCyclingSlot1458> inventory, out int slot)
    {
        slot = -1;
        if (weaponType is < 0 or >= 6196 || ammoFamily <= 0 ||
            mode > AmmoCyclingMode1458.Random || inventory.Length != InventorySlotCount) return false;
        foreach (var item in inventory)
            if (item.AmmoFamily < 0) return false;

        if (mode == AmmoCyclingMode1458.AmmoSlots)
        {
            slot = SelectRange(weaponType, ammoFamily, mode, state.Offset, inventory,
                ammoFamily == CoinAmmoFamily ? CoinsOrder : AmmoOrder, true);
            if (slot >= 0) return true;
        }
        slot = SelectRange(weaponType, ammoFamily, mode, state.Offset, inventory, DefaultOrder,
            mode is AmmoCyclingMode1458.FullInventory or AmmoCyclingMode1458.Random);
        return true; // -1 is known source absence, not unknown metadata.
    }

    private static int SelectRange(int weaponType, int ammoFamily, AmmoCyclingMode1458 mode,
        int offset, ReadOnlySpan<AmmoCyclingSlot1458> inventory, ReadOnlySpan<int> order, bool allowCycling)
    {
        int skip = 0;
        if (allowCycling && mode != AmmoCyclingMode1458.None)
        {
            int matches = 0;
            foreach (int index in order)
                if (inventory[index].AmmoFamily == ammoFamily && inventory[index].Stack > 0) matches++;
            if (matches == 0) return -1;
            skip = offset % matches;
            if (weaponType is 779 or 5134) skip = 0;
        }
        foreach (int index in order)
        {
            if (inventory[index].AmmoFamily != ammoFamily || inventory[index].Stack <= 0) continue;
            if (skip == 0) return index;
            skip--;
        }
        return -1;
    }

    /// <summary>
    /// Call only after a real selected compatible ammo item. This precedes
    /// conservation and launch entropy even when the selected item is conserved.
    /// Missing ammo and gunProj/dontConsume do not execute this transition.
    /// The delegate must be the caller's existing detached source RNG preview.
    /// </summary>
    public static bool TryAdvanceSelectedAmmo(int sourceUseAnimation, bool dontConsume,
        AmmoCyclingMode1458 mode, in AmmoCyclingState1458 before, Func<int> next,
        out AmmoCyclingState1458 after)
    {
        after = before;
        if (sourceUseAnimation < 0 || mode > AmmoCyclingMode1458.Random) return false;
        if (dontConsume || mode == AmmoCyclingMode1458.None) return true;
        ArgumentNullException.ThrowIfNull(next);
        int offset = unchecked(before.Offset + 1);
        if (mode == AmmoCyclingMode1458.Random) offset = next();
        after = new(offset, unchecked(sourceUseAnimation + SourceCooldownTail));
        return true;
    }
}
