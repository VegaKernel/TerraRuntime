using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public enum InventoryCursorTransition1458 : byte
{
    None,
    WholeExchangeOrMerge,
    SingleToCursor,
    RepeatedToCursor
}

/// <summary>
/// Detached ordinary0/coin1, bank4 and void-bank32 tuple transitions. UI gates and funding adoption
/// remain with their callers; packet-5 reports do not create quantity provenance.
/// </summary>
public static class VanillaInventoryCursorMove1458
{
    public const int OrdinaryContext = 0;
    public const int CoinContext = 1;
    public const int WorldChestContext = 3;
    public const int BankContext = 4;
    public const int VoidBankContext = 32;
    public static bool TryPlanLeftClick(in WorldItemPickupSlot1458 slot,
        in WorldItemPickupSlot1458 cursor, out WorldItemPickupSlot1458 afterSlot,
        out WorldItemPickupSlot1458 afterCursor)
        => TryPlanLeftClick(in slot, in cursor, out afterSlot, out afterCursor, OrdinaryContext);

    public static bool TryPlanLeftClick(in WorldItemPickupSlot1458 slot,
        in WorldItemPickupSlot1458 cursor, out WorldItemPickupSlot1458 afterSlot,
        out WorldItemPickupSlot1458 afterCursor, int bankContext)
    {
        afterSlot = afterCursor = default;
        if (!IsContext(bankContext)) return false;
        if (!IsValid(in slot) || !IsValid(in cursor) || (slot.Type == 0 && cursor.Type == 0))
            return false;

        if (bankContext == CoinContext && cursor.Type != 0 &&
            !VanillaCoinFacts.TryGetValue(new ItemTypeId(cursor.Type), out _))
            return false;

        afterSlot = cursor;
        afterCursor = slot;
        if (slot.Type != 0 && cursor.Type == slot.Type && cursor.Prefix == slot.Prefix)
        {
            _ = VanillaWorldItemAllocationCatalog1458.TryGet(slot.Type, out var facts);
            // The compatible-stack favorite swap precedes the capacity guard, even for full stacks.
            afterSlot = afterSlot with { Favorite = slot.Favorite };
            afterCursor = afterCursor with { Favorite = cursor.Favorite };
            // Original LeftClick swaps first. A full stack on either side remains an exchange.
            if (slot.Stack != facts.MaximumStack && cursor.Stack != facts.MaximumStack)
            {
                int moved = Math.Min(slot.Stack, facts.MaximumStack - cursor.Stack);
                afterSlot = cursor with { Stack = cursor.Stack + moved, Favorite = slot.Favorite };
                int remaining = slot.Stack - moved;
                afterCursor = remaining == 0 ? default : slot with { Stack = remaining, Favorite = cursor.Favorite };
            }
        }
        if (bankContext is BankContext or WorldChestContext)
        {
            // Original context4 clears both resulting favorites; void context32
            // keeps the ordinary context0 favorite transition.
            afterSlot = afterSlot with { Favorite = false };
            afterCursor = afterCursor with { Favorite = false };
        }
        return afterSlot != slot || afterCursor != cursor;
    }

    public static bool TryPlanSingleToCursor(in WorldItemPickupSlot1458 slot,
        in WorldItemPickupSlot1458 cursor, out WorldItemPickupSlot1458 afterSlot,
        out WorldItemPickupSlot1458 afterCursor)
        => TryPlanToCursor(in slot, in cursor, 1, out afterSlot, out afterCursor);

    public static bool TryPlanSingleToCursor(in WorldItemPickupSlot1458 slot,
        in WorldItemPickupSlot1458 cursor, out WorldItemPickupSlot1458 afterSlot,
        out WorldItemPickupSlot1458 afterCursor, int bankContext)
        => TryPlanToCursor(in slot, in cursor, 1, out afterSlot, out afterCursor, bankContext);

    public static bool TryPlanToCursor(in WorldItemPickupSlot1458 slot,
        in WorldItemPickupSlot1458 cursor, int amount, out WorldItemPickupSlot1458 afterSlot,
        out WorldItemPickupSlot1458 afterCursor)
        => TryPlanToCursor(in slot, in cursor, amount, out afterSlot, out afterCursor, OrdinaryContext);

    public static bool TryPlanToCursor(in WorldItemPickupSlot1458 slot,
        in WorldItemPickupSlot1458 cursor, int amount, out WorldItemPickupSlot1458 afterSlot,
        out WorldItemPickupSlot1458 afterCursor, int bankContext)
    {
        afterSlot = afterCursor = default;
        if (!IsContext(bankContext)) return false;
        if (!IsValid(in slot) || !IsValid(in cursor) || slot.Type == 0 || amount <= 0 || amount > slot.Stack)
            return false;
        _ = VanillaWorldItemAllocationCatalog1458.TryGet(slot.Type, out var facts);
        if (cursor.Type != 0 && (cursor.Type != slot.Type || cursor.Prefix != slot.Prefix)) return false;
        if (amount > facts.MaximumStack - cursor.Stack) return false;

        // Only the first empty-cursor clone sets favorite. Later one-unit calls retain it,
        // so exhausting a favorited multi-stack does not turn the cursor favorite on.
        afterCursor = cursor.Type == 0
            ? slot with { Stack = amount, Favorite = slot.Favorite && slot.Stack == 1 }
            : cursor with { Stack = cursor.Stack + amount };
        int remaining = slot.Stack - amount;
        afterSlot = remaining == 0 ? default : slot with { Stack = remaining };
        return true;
    }

    public static bool TryResolveReportedTransition(in WorldItemPickupSlot1458 slot,
        in WorldItemPickupSlot1458 cursor, in WorldItemPickupSlot1458 reportedSlot,
        in WorldItemPickupSlot1458 reportedCursor, out InventoryCursorTransition1458 transition)
        => TryResolveReportedTransition(in slot, in cursor, in reportedSlot, in reportedCursor, out transition, OrdinaryContext);

    public static bool TryResolveReportedTransition(in WorldItemPickupSlot1458 slot,
        in WorldItemPickupSlot1458 cursor, in WorldItemPickupSlot1458 reportedSlot,
        in WorldItemPickupSlot1458 reportedCursor, out InventoryCursorTransition1458 transition, int bankContext)
    {
        transition = InventoryCursorTransition1458.None;
        if (!IsContext(bankContext)) return false;
        if (!IsValid(in reportedSlot) || !IsValid(in reportedCursor)) return false;
        if (TryPlanLeftClick(in slot, in cursor, out var leftSlot, out var leftCursor, bankContext) &&
            leftSlot == reportedSlot && leftCursor == reportedCursor)
        {
            transition = InventoryCursorTransition1458.WholeExchangeOrMerge;
            return true;
        }
        int amount = slot.Stack - reportedSlot.Stack;
        if (TryPlanToCursor(in slot, in cursor, amount, out var singleSlot, out var singleCursor, bankContext) &&
            singleSlot == reportedSlot && singleCursor == reportedCursor)
        {
            transition = amount == 1 ? InventoryCursorTransition1458.SingleToCursor : InventoryCursorTransition1458.RepeatedToCursor;
            return true;
        }
        return false;
    }

    private static bool IsContext(int context) => context is OrdinaryContext or CoinContext or WorldChestContext or BankContext or VoidBankContext;

    private static bool IsValid(in WorldItemPickupSlot1458 item)
    {
        if (item.Type == 0) return item == default; // gameplay-domain-literal-audit: allow raw-entity-type-decision - Zero validates the canonical Air representation, not a content-specific rule.
        // This source tuple transform does not assign value custody or perform DoCoins conversion.
        return item.Type > 0 && item.Type < VanillaItemIds.Count && // gameplay-domain-literal-audit: allow raw-entity-type-decision - Canonical storage rejects signed variant IDs before retained-identity validation.
            (!VanillaCoinFacts.TryGetValue(new ItemTypeId(item.Type), out _) || item.Prefix == VanillaPrefixIds.NoneValue) &&
            item.Prefix is >= 0 and <= byte.MaxValue && item.Stack > 0 &&
            VanillaItemRetainedIdentity1458.TryResolve(new ItemTypeId(item.Type), out var retained) &&
            retained.Value == item.Type && VanillaWorldItemAllocationCatalog1458.TryGet(item.Type, out var facts) &&
            (VanillaCoinFacts.TryGetValue(new ItemTypeId(item.Type), out _)
                ? VanillaCoinFacts.IsValidRetainedStack(new ItemTypeId(item.Type), item.Stack)
                : item.Stack <= facts.MaximumStack);
    }
}
