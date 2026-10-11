using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct ShopItem1458(WorldItemPickupSlot1458 Tuple, int Value,
    bool BuyOnce, bool IsShopItem, int? CustomPrice = null, int? Currency = null);
public readonly record struct ShopMemo1458(int Type, int Prefix, int Stack);
public readonly record struct ShopPriceContext1458(float Adjustment, bool Discount,
    bool WindowsItemPrefixArithmetic = false, VanillaItemPrefixWorld1458? World = null);
public sealed record ShopTransactionPlan1458(ShopWalletPlan1458 Wallet, ShopItem1458[] Stock,
    ShopItem1458 Cursor, ShopMemo1458[] Memos, int Units, long CopperDelta);

/// <summary>Detached official1.4.5.8 shop cursor, finite-stock and sellback state transitions.</summary>
public static partial class VanillaShopTransaction1458
{
    public const int StockCount = 40;
    private const int ResaleSlots = StockCount - 1;
    // One source shop contains forty offers. This policy refuses malformed imported
    // memo growth instead of reproducing unbounded or overflowing source bookkeeping.
    private const int MaximumMemos = StockCount;

    public static bool TryPlanBuy(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks, ReadOnlySpan<ShopItem1458> stock,
        ReadOnlySpan<ShopMemo1458> memos, in ShopItem1458 cursor, int slot, int requestedUnits,
        in ShopPriceContext1458 context, out ShopTransactionPlan1458? plan,
        int itemAnimation = 0, int itemTime = 0, bool rightClick = false, int stackSplit = 0)
    {
        plan = null;
        if ((uint)slot >= StockCount || !Validate(stock, memos, in cursor) ||
            inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            inventory[VanillaPlayerItemSlotCatalog.InventoryMouseItem] != cursor.Tuple) return false;
        var offer = stock[slot];
        if (offer.Tuple.Type == 0 || !Prices(in offer, in context, out _, out long price) ||
            !VanillaShopWallet1458.TryPlanCursorPurchase(inventory, banks, offer.Tuple,
                offer.BuyOnce, price, requestedUnits, out var purchase,
                itemAnimation, itemTime, rightClick, stackSplit)) return false;
        var projectedStock = stock.ToArray();
        projectedStock[slot] = purchase!.Stock.Type == 0 ? default : offer with { Tuple = purchase.Stock };
        var projectedMemos = memos.ToArray().ToList();
        if (!offer.BuyOnce)
        {
            int index = FindMemo(projectedMemos, offer.Tuple.Type, offer.Tuple.Prefix);
            if (index >= 0)
            {
                long next = (long)projectedMemos[index].Stack + purchase.SellbackAdded;
                if (next > int.MaxValue) return false;
                projectedMemos[index] = projectedMemos[index] with { Stack = (int)next };
            }
            else
            {
                if (projectedMemos.Count == MaximumMemos) return false;
                projectedMemos.Add(new(offer.Tuple.Type, offer.Tuple.Prefix, purchase.SellbackAdded));
            }
        }
        ShopItem1458 projectedCursor;
        if (cursor.Tuple.Type == 0)
        {
            // Source mouse birth uses SetDefaults and the represented forced prefix,
            // not a clone of the shop's buyOnce/custom price/value metadata.
            if (!VanillaItemStoreValue1458.TryGet(new(offer.Tuple.Type), new(offer.Tuple.Prefix),
                    out int value, context.WindowsItemPrefixArithmetic, context.World)) return false;
            projectedCursor = new(purchase.Cursor, value, false, false);
        }
        else projectedCursor = cursor with { Tuple = purchase.Cursor };
        plan = new(purchase.Wallet, projectedStock, projectedCursor,
            projectedMemos.ToArray(), purchase.Units, -purchase.Wallet.Copper);
        return true;
    }

    public static bool TryPlanSell(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks, ReadOnlySpan<ShopItem1458> stock,
        ReadOnlySpan<ShopMemo1458> memos, in ShopItem1458 cursor,
        in ShopPriceContext1458 context, out ShopTransactionPlan1458? plan,
        int itemAnimation = 0, int itemTime = 0)
    {
        plan = null;
        if (itemAnimation != 0 || itemTime != 0 || !Validate(stock, memos, in cursor) ||
            inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            inventory[VanillaPlayerItemSlotCatalog.InventoryMouseItem] != cursor.Tuple ||
            cursor.Tuple.Type == 0 || !Prices(in cursor, in context, out long selling, out long buying)) return false;
        var projectedMemos = memos.ToArray().ToList();
        int memoIndex = FindMemo(projectedMemos, cursor.Tuple.Type, cursor.Tuple.Prefix);
        int memoAmount = memoIndex < 0 ? 0 : projectedMemos[memoIndex].Stack;
        if (!VanillaShopWallet1458.TryPlanCursorSale(inventory, banks, selling, buying,
                cursor.Value, memoAmount, out var payout)) return false;
        int refunded = Math.Min(memoAmount, cursor.Tuple.Stack);
        if (memoIndex >= 0)
        {
            if (memoAmount <= cursor.Tuple.Stack) projectedMemos.RemoveAt(memoIndex);
            else projectedMemos[memoIndex] = projectedMemos[memoIndex] with { Stack = memoAmount - cursor.Tuple.Stack };
        }
        int remaining = cursor.Tuple.Stack - refunded;
        var projectedStock = stock.ToArray();
        // Chest.AddItemToShop merges finite resale slots before the first free slot.
        // Slot39 is the sale UI. No shift or fabricated resale offer occurs on full stock.
        for (int destination = 0; destination < ResaleSlots && remaining > 0; destination++)
        {
            var existing = projectedStock[destination];
            if (!existing.BuyOnce || existing.Tuple.Type != cursor.Tuple.Type || existing.Tuple.Prefix != cursor.Tuple.Prefix ||
                !VanillaWorldItemAllocationCatalog1458.TryGet(existing.Tuple.Type, out var facts)) continue;
            int amount = Math.Min(remaining, facts.MaximumStack - existing.Tuple.Stack);
            if (amount <= 0) continue;
            projectedStock[destination] = existing with { Tuple = existing.Tuple with { Stack = existing.Tuple.Stack + amount } };
            remaining -= amount;
        }
        if (remaining > 0)
        {
            for (int destination = 0; destination < ResaleSlots; destination++)
            {
                if (projectedStock[destination].Tuple.Type != 0) continue;
                projectedStock[destination] = cursor with
                {
                    Tuple = cursor.Tuple with { Stack = remaining, Favorite = false },
                    BuyOnce = true
                };
                break;
            }
        }
        plan = new(payout!, projectedStock, default, projectedMemos.ToArray(), cursor.Tuple.Stack, payout!.Copper);
        return true;
    }

    public static bool TryGetMemoIdentity(ItemTypeId type, out int key)
    {
        key = 0;
        if (!VanillaItemRetainedIdentity1458.TryResolve(type, out var retained) || retained == default) return false;
        key = MemoKey(retained.Value);
        return true;
    }

    private static bool Prices(in ShopItem1458 item, in ShopPriceContext1458 context,
        out long selling, out long buying)
    {
        selling = buying = 0;
        return item.Currency is null &&
            VanillaItemStoreValue1458.TryGet(new(item.Tuple.Type), new(item.Tuple.Prefix), out _, context.WindowsItemPrefixArithmetic, context.World) &&
            VanillaShopWallet1458.TryResolvePrices(
            item.CustomPrice ?? item.Value, context.Adjustment, context.Discount,
            item.BuyOnce, item.IsShopItem, out selling, out buying, context.WindowsItemPrefixArithmetic);
    }

    private static int FindMemo(List<ShopMemo1458> memos, int type, int prefix)
    {
        int key = MemoKey(type);
        for (int index = 0; index < memos.Count; index++)
            if (MemoKey(memos[index].Type) == key && memos[index].Prefix == prefix) return index;
        return -1;
    }

    private static bool Validate(ReadOnlySpan<ShopItem1458> stock,
        ReadOnlySpan<ShopMemo1458> memos, in ShopItem1458 cursor)
    {
        if (stock.Length != StockCount || memos.Length > MaximumMemos || !ValidItem(in cursor)) return false;
        foreach (ref readonly var item in stock) if (!ValidItem(in item)) return false;
        for (int index = 0; index < memos.Length; index++)
        {
            var memo = memos[index];
            if (memo.Stack <= 0 || (uint)memo.Prefix > 97 ||
                !TryGetMemoIdentity(new(memo.Type), out int key)) return false;
            for (int earlier = 0; earlier < index; earlier++)
                if (MemoKey(memos[earlier].Type) == key && memos[earlier].Prefix == memo.Prefix) return false;
        }
        return true;
    }

    private static bool ValidItem(in ShopItem1458 item)
    {
        if (item.Tuple.Type == 0) return item == default; // gameplay-domain-literal-audit: allow raw-entity-type-decision - Zero validates the canonical empty shop item.
        return item.Value >= 0 && item.CustomPrice is null or >= 0 && item.Currency is null or >= 0 &&
            item.Tuple.Stack > 0 && (uint)item.Tuple.Prefix <= 97 &&
            VanillaItemRetainedIdentity1458.TryResolve(new(item.Tuple.Type), out var retained) &&
            retained.Value == item.Tuple.Type &&
            VanillaWorldItemAllocationCatalog1458.TryGet(item.Tuple.Type, out var allocation) &&
            item.Tuple.Stack <= allocation.MaximumStack;
    }
}
