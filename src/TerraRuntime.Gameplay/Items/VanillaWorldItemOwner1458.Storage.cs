using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct WorldItemStorageMutation1458(short Slot,
    WorldItemPickupSlot1458 Before, WorldItemPickupSlot1458 After);

public sealed record WorldItemStoragePlan1458(WorldItemPickupSlot1458 Residual,
    int Taken, WorldItemStorageMutation1458[] Mutations);

public static partial class VanillaWorldItemOwner1458
{
    /// <summary>
    /// Detached Player.GetItem storage, pinned to 1.4.5.8. Coin conversion is staged; no
    /// pickup effects, void fallback, callbacks or random offers are owned here.
    /// Runtime adoption/publication policy belongs to the existing inventory/world owners.
    /// </summary>
    public static bool TryPlanStorage(in WorldItemPickupSlot1458 incoming,
        ReadOnlySpan<WorldItemPickupSlot1458> inventory, bool voidEnabled,
        VanillaItemPrefixWorld1458? world, out WorldItemStoragePlan1458? plan)
        => TryPlanStorage(in incoming, inventory, voidEnabled, world, out plan, false);

    /// <summary>Source GetItem settings variant. NoCoinMerge suppresses DoCoins while retaining coin slot priority.</summary>
    public static bool TryPlanStorage(in WorldItemPickupSlot1458 incoming,
        ReadOnlySpan<WorldItemPickupSlot1458> inventory, bool voidEnabled,
        VanillaItemPrefixWorld1458? world, out WorldItemStoragePlan1458? plan, bool noCoinMerge)
    {
        plan = null;
        if (inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            !IsCanonicalStorageSlot(in incoming) || incoming.Type == 0 ||
            !VanillaWorldItemPickupCatalog1458.TryGet(incoming.Type, out var facts) ||
            facts.Pickup || facts.NotInventory)
            return false;
        for (int slot = 0; slot < inventory.Length; slot++)
            if (!IsCanonicalStorageSlot(in inventory[slot])) return false;

        if (incoming.Type is >= 71 and <= 74)
            return TryPlanCoinStorage(in incoming, inventory, voidEnabled, out plan, noCoinMerge);

        bool usable = facts.HasUseStyle;
        if (incoming.Type is 544 or 556 or 557 or 5334)
        {
            // Original ItemVariants changes only these four storage-useStyle selectors.
            // Unknown world context must not be read as the normal world.
            if (world is not { } context) return false;
            bool mechdusa = context.RemixWorld && context.GetGoodWorld;
            usable = incoming.Type == 5334 ? mechdusa : !mechdusa;
        }

        if (facts.UniqueStack)
            foreach (var slot in inventory[..VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount])
                if (slot.Type == incoming.Type && slot.Stack > 0)
                {
                    plan = new(incoming, 0, []);
                    return true;
                }

        int remaining = incoming.Stack;
        var mutations = new List<WorldItemStorageMutation1458>(VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount);
        if (facts.FitsAmmoSlot)
        {
            // FillAmmo precedes ordinary merges. It does not honor the ordinary
            // favorited/OnlyNeedOne merge barrier, and an empty ammo slot wins first.
            for (short slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
                slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive && remaining > 0; slot++)
            {
                var before = inventory[slot];
                if (before.Type != incoming.Type || before.Prefix != incoming.Prefix) continue;
                if (!VanillaWorldItemAllocationCatalog1458.TryGet(before.Type, out var allocation)) return false;
                int amount = Math.Min(remaining, allocation.MaximumStack - before.Stack);
                if (amount <= 0) continue;
                mutations.Add(new(slot, before, before with { Stack = before.Stack + amount }));
                remaining -= amount;
            }
            if (facts.EmptyAmmo && remaining > 0)
                for (short slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
                    slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; slot++)
                    if (inventory[slot].Type == 0)
                    {
                        mutations.Add(new(slot, inventory[slot], incoming with { Stack = remaining }));
                        remaining = 0;
                        break;
                    }
        }
        for (short slot = 0; slot < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive && remaining > 0; slot++)
        {
            var before = inventory[slot];
            if (before.Type != incoming.Type || before.Prefix != incoming.Prefix ||
                (before.Favorite && facts.OnlyNeedOne)) continue;
            if (!VanillaWorldItemAllocationCatalog1458.TryGet(before.Type, out var allocation)) return false;
            int amount = Math.Min(remaining, allocation.MaximumStack - before.Stack);
            if (amount <= 0) continue;
            mutations.Add(new(slot, before, before with { Stack = before.Stack + amount }));
            remaining -= amount;
        }

        if (remaining > 0 && usable)
            for (short slot = 0; slot < 10; slot++)
                if (inventory[slot].Type == 0)
                {
                    mutations.Add(new(slot, inventory[slot], incoming with { Stack = remaining }));
                    remaining = 0;
                    break;
                }
        if (remaining > 0)
        {
            int start = incoming.Favorite ? 0 : VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive - 1;
            int increment = incoming.Favorite ? 1 : -1;
            for (int slot = start; (uint)slot < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive; slot += increment)
                if (inventory[slot].Type == 0)
                {
                    mutations.Add(new((short)slot, inventory[slot], incoming with { Stack = remaining }));
                    remaining = 0;
                    break;
                }
        }
        // A nonempty residual could enter bank4 in original GetItem. We do not pretend
        // the ordinary plan is complete while that additional storage owner is enabled.
        if (remaining > 0 && voidEnabled) return false;
        plan = new(remaining == 0 ? default : incoming with { Stack = remaining },
            incoming.Stack - remaining, mutations.ToArray());
        return true;
    }

    private static bool IsCanonicalStorageSlot(in WorldItemPickupSlot1458 item)
    {
        if (item.Type == 0) return item == default; // gameplay-domain-literal-audit: allow raw-entity-type-decision - Zero validates the canonical Air representation, not a content-specific rule.
        // GetItem retains the independently validated source prefix; unlike wallet/cursor
        // admission, this branch uses only the common positive-wire coin stack bound.
        return item.Type > 0 && item.Stack > 0 && item.Prefix >= 0 && item.Prefix < VanillaItemPrefixCatalog.Count && // gameplay-domain-literal-audit: allow raw-entity-type-decision - Canonical storage rejects signed variant IDs before retained-identity validation.
            VanillaItemRetainedIdentity1458.TryResolve(new ItemTypeId(item.Type), out var retained) &&
            retained.Value == item.Type &&
            VanillaWorldItemAllocationCatalog1458.TryGet(item.Type, out var allocation) &&
            (VanillaCoinFacts.TryGetValue(new ItemTypeId(item.Type), out _)
                ? VanillaCoinFacts.IsValidRetainedStack(new ItemTypeId(item.Type), item.Stack)
                : item.Stack <= allocation.MaximumStack);
    }
}
