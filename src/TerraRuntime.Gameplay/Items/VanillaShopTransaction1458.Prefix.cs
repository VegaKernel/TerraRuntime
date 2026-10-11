using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public static partial class VanillaShopTransaction1458
{
    /// <summary>
    /// Prepares source-ordered buy prefixes and scratch sellback images without
    /// per-prefix detached plans. Views are borrowed until the next advance;
    /// callers must detach a selected result before retaining or adopting it.
    /// </summary>
    public static bool TryCreateBuySellbackPrefixWalker(
        ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks,
        ReadOnlySpan<ShopItem1458> stock, ReadOnlySpan<ShopMemo1458> memos,
        in ShopItem1458 cursor, int slot, in ShopPriceContext1458 context,
        int maximumUnits, out BuySellbackPrefixWalker? walker,
        int itemAnimation = 0, int itemTime = 0)
    {
        walker = null;
        if ((uint)slot >= StockCount || maximumUnits < 1 || maximumUnits > VanillaCoinFacts.PlatinumMaximumStack ||
            itemAnimation != 0 || itemTime != 0 || cursor != default ||
            !Validate(stock, memos, in cursor) || inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            inventory[VanillaPlayerItemSlotCatalog.InventoryMouseItem] != default) return false;
        var offer = stock[slot];
        if (offer.Tuple.Type == 0 || !Prices(in offer, in context, out _, out long price) ||
            !VanillaWorldItemAllocationCatalog1458.TryGet(offer.Tuple.Type, out var facts) ||
            !VanillaItemStoreValue1458.TryGet(new(offer.Tuple.Type), new(offer.Tuple.Prefix), out int value,
                context.WindowsItemPrefixArithmetic, context.World)) return false;
        var born = new ShopItem1458(offer.Tuple with { Stack = 1, Favorite = false }, value, false, false);
        if (!Prices(in born, in context, out long selling, out long buying) ||
            !VanillaShopWallet1458.PrefixWork.TryCreate(inventory, banks, out var work) || work is null) return false;
        int limit = Math.Min(maximumUnits, facts.MaximumStack);
        if (offer.BuyOnce) limit = Math.Min(limit, offer.Tuple.Stack);
        walker = new(work, stock, memos, slot, limit, price, selling, buying, in born);
        return true;
    }

    public sealed class BuySellbackPrefixWalker
    {
        private readonly VanillaShopWallet1458.PrefixWork work;
        private readonly ShopItem1458[] stock;
        private readonly ShopItem1458[] soldStock;
        private readonly ShopMemo1458[] memos = new ShopMemo1458[MaximumMemos];
        private readonly ShopMemo1458[] soldMemos = new ShopMemo1458[MaximumMemos];
        private readonly int slot;
        private readonly int limit;
        private readonly long price;
        private readonly long selling;
        private readonly long buying;
        private readonly ShopItem1458 born;
        private int memoCount;
        private int soldMemoCount;
        private bool ended;

        internal BuySellbackPrefixWalker(VanillaShopWallet1458.PrefixWork work,
            ReadOnlySpan<ShopItem1458> stock, ReadOnlySpan<ShopMemo1458> memos,
            int slot, int limit, long price, long selling, long buying, in ShopItem1458 born)
        {
            this.work = work;
            this.stock = stock.ToArray();
            soldStock = new ShopItem1458[StockCount];
            memos.CopyTo(this.memos);
            memoCount = memos.Length;
            this.slot = slot;
            this.limit = limit;
            this.price = price;
            this.selling = selling;
            this.buying = buying;
            this.born = born;
        }

        public int Units { get; private set; }
        public long ChargedCopper { get; private set; }
        public long GrantedCopper { get; private set; }
        public long NetCopperDelta => GrantedCopper - ChargedCopper;
        public bool CanSellCurrent { get; private set; }
        public ReadOnlySpan<WorldItemPickupSlot1458> BoughtInventory => work.Inventory;
        public ReadOnlySpan<WorldItemPickupSlot1458> BoughtBanks => work.Banks;
        public ReadOnlySpan<ShopItem1458> BoughtStock => stock;
        public ReadOnlySpan<ShopMemo1458> BoughtMemos => memos.AsSpan(0, memoCount);
        public ShopItem1458 BoughtCursor { get; private set; }
        public ReadOnlySpan<WorldItemPickupSlot1458> SoldInventory => work.SoldInventory;
        public ReadOnlySpan<WorldItemPickupSlot1458> SoldBanks => work.Banks;
        public ReadOnlySpan<ShopItem1458> SoldStock => soldStock;
        public ReadOnlySpan<ShopMemo1458> SoldMemos => soldMemos.AsSpan(0, soldMemoCount);
        public ShopItem1458 SoldCursor => default;

        // A purchased prefix can exist even when its scratch sale has no room.
        // CanSellCurrent makes that distinction explicit; later prefixes still advance.
        public bool TryAdvance()
        {
            if (ended || Units >= limit) return false;
            var offer = stock[slot];
            int index = FindCurrentMemo(offer.Tuple.Type, offer.Tuple.Prefix);
            if (!offer.BuyOnce &&
                ((index < 0 && memoCount == MaximumMemos) ||
                 (index >= 0 && (long)memos[index].Stack + offer.Tuple.Stack > int.MaxValue)))
            {
                ended = true;
                return false;
            }
            if (!work.TryPayNext(price))
            {
                ended = true;
                return false;
            }
            Units++;
            ChargedCopper = checked(ChargedCopper + price);
            BoughtCursor = born with { Tuple = born.Tuple with { Stack = Units } };
            work.SetCursor(BoughtCursor.Tuple);
            if (offer.BuyOnce)
                stock[slot] = offer.Tuple.Stack == 1 ? default : offer with { Tuple = offer.Tuple with { Stack = offer.Tuple.Stack - 1 } };
            else if (index >= 0)
                memos[index] = memos[index] with { Stack = memos[index].Stack + offer.Tuple.Stack };
            else
                memos[memoCount++] = new(offer.Tuple.Type, offer.Tuple.Prefix, offer.Tuple.Stack);

            index = FindCurrentMemo(BoughtCursor.Tuple.Type, BoughtCursor.Tuple.Prefix);
            int memoAmount = index < 0 ? 0 : memos[index].Stack;
            CanSellCurrent = work.TrySellCurrent(selling, buying, born.Value, Units, memoAmount, out long grant);
            GrantedCopper = grant;
            if (!CanSellCurrent) return true;
            stock.CopyTo(soldStock, 0);
            memos.CopyTo(soldMemos, 0);
            soldMemoCount = memoCount;
            int refunded = Math.Min(memoAmount, Units);
            if (index >= 0)
            {
                if (memoAmount <= Units)
                {
                    soldMemos.AsSpan(index + 1, soldMemoCount - index - 1).CopyTo(soldMemos.AsSpan(index));
                    soldMemos[--soldMemoCount] = default;
                }
                else soldMemos[index] = soldMemos[index] with { Stack = memoAmount - Units };
            }
            int remaining = Units - refunded;
            for (int destination = 0; destination < ResaleSlots && remaining > 0; destination++)
            {
                var existing = soldStock[destination];
                if (!existing.BuyOnce || existing.Tuple.Type != BoughtCursor.Tuple.Type ||
                    existing.Tuple.Prefix != BoughtCursor.Tuple.Prefix ||
                    !VanillaWorldItemAllocationCatalog1458.TryGet(existing.Tuple.Type, out var facts)) continue;
                int amount = Math.Min(remaining, facts.MaximumStack - existing.Tuple.Stack);
                if (amount <= 0) continue;
                soldStock[destination] = existing with { Tuple = existing.Tuple with { Stack = existing.Tuple.Stack + amount } };
                remaining -= amount;
            }
            if (remaining > 0)
                for (int destination = 0; destination < ResaleSlots; destination++)
                    if (soldStock[destination].Tuple.Type == 0)
                    {
                        soldStock[destination] = BoughtCursor with
                        {
                            Tuple = BoughtCursor.Tuple with { Stack = remaining, Favorite = false },
                            BuyOnce = true
                        };
                        break;
                    }
            return true;
        }

        public bool TryDetachCurrent(out ShopTransactionPlan1458? bought, out ShopTransactionPlan1458? sold)
        {
            bought = sold = null;
            if (Units == 0 || !CanSellCurrent) return false;
            bought = new(work.DetachBought(ChargedCopper), stock.ToArray(), BoughtCursor,
                BoughtMemos.ToArray(), Units, -ChargedCopper);
            sold = new(work.DetachSold(GrantedCopper), soldStock.ToArray(), default,
                SoldMemos.ToArray(), Units, GrantedCopper);
            return true;
        }

        private int FindCurrentMemo(int type, int prefix)
        {
            int key = MemoKey(type);
            for (int index = 0; index < memoCount; index++)
                if (MemoKey(memos[index].Type) == key && memos[index].Prefix == prefix) return index;
            return -1;
        }
    }

    /// <summary>
    /// Prepares one source purchase per advance, including a compatible existing
    /// cursor and the separate Left/Right clock gates. Views remain borrowed;
    /// detach a selected prefix before retaining or adopting it.
    /// </summary>
    public static bool TryCreatePurchasePrefixWalker(
        ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks,
        ReadOnlySpan<ShopItem1458> stock, ReadOnlySpan<ShopMemo1458> memos,
        in ShopItem1458 cursor, int slot, in ShopPriceContext1458 context,
        int maximumUnits, out PurchasePrefixWalker? walker,
        int itemAnimation = 0, int itemTime = 0, bool rightClick = false, int stackSplit = 0)
    {
        walker = null;
        if ((uint)slot >= StockCount || maximumUnits < 1 ||
            maximumUnits > VanillaCoinFacts.PlatinumMaximumStack ||
            itemAnimation != 0 || itemTime < 0 || (!rightClick && itemTime != 0) || stackSplit > 1 ||
            !Validate(stock, memos, in cursor) ||
            inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            inventory[VanillaPlayerItemSlotCatalog.InventoryMouseItem] != cursor.Tuple) return false;
        var offer = stock[slot];
        if (offer.Tuple.Type == 0 || !Prices(in offer, in context, out _, out long price) ||
            !VanillaWorldItemAllocationCatalog1458.TryGet(offer.Tuple.Type, out var facts) ||
            (cursor.Tuple.Type != 0 &&
             (cursor.Tuple.Type != offer.Tuple.Type || cursor.Tuple.Prefix != offer.Tuple.Prefix))) return false;
        int limit = Math.Min(maximumUnits, facts.MaximumStack - cursor.Tuple.Stack);
        if (offer.BuyOnce) limit = Math.Min(limit, offer.Tuple.Stack);
        if (limit <= 0) return false;
        var born = cursor;
        if (cursor.Tuple.Type == 0)
        {
            if (!VanillaItemStoreValue1458.TryGet(new(offer.Tuple.Type), new(offer.Tuple.Prefix),
                    out int value, context.WindowsItemPrefixArithmetic, context.World)) return false;
            // Source empty-cursor birth uses stored item defaults and the represented
            // forced prefix, not the offer's custom price or finite-stock metadata.
            born = new(offer.Tuple with { Stack = 1, Favorite = false }, value, false, false);
        }
        if (!VanillaShopWallet1458.PrefixWork.TryCreate(inventory, banks, out var work) || work is null) return false;
        walker = new(work, stock, memos, in cursor, in born, slot, limit, price);
        return true;
    }

    public sealed class PurchasePrefixWalker
    {
        private readonly VanillaShopWallet1458.PrefixWork work;
        private readonly ShopItem1458[] stock;
        private readonly ShopMemo1458[] memos = new ShopMemo1458[MaximumMemos];
        private readonly ShopItem1458 born;
        private readonly int slot;
        private readonly int limit;
        private readonly long price;
        private int memoCount;
        private bool ended;

        internal PurchasePrefixWalker(VanillaShopWallet1458.PrefixWork work,
            ReadOnlySpan<ShopItem1458> stock, ReadOnlySpan<ShopMemo1458> memos,
            in ShopItem1458 cursor, in ShopItem1458 born, int slot, int limit, long price)
        {
            this.work = work;
            this.stock = stock.ToArray();
            memos.CopyTo(this.memos);
            memoCount = memos.Length;
            BoughtCursor = cursor;
            this.born = born;
            this.slot = slot;
            this.limit = limit;
            this.price = price;
        }

        public int Units { get; private set; }
        public long ChargedCopper { get; private set; }
        public ReadOnlySpan<WorldItemPickupSlot1458> BoughtInventory => work.Inventory;
        public ReadOnlySpan<WorldItemPickupSlot1458> BoughtBanks => work.Banks;
        public ReadOnlySpan<ShopItem1458> BoughtStock => stock;
        public ReadOnlySpan<ShopMemo1458> BoughtMemos => memos.AsSpan(0, memoCount);
        public ShopItem1458 BoughtCursor { get; private set; }

        // Payment rolls back only the unsuccessful next unit. Earlier source
        // purchases remain available; no advance executes the remaining batch.
        public bool TryAdvance()
        {
            if (ended || Units >= limit) return false;
            var offer = stock[slot];
            int index = FindCurrentMemo(offer.Tuple.Type, offer.Tuple.Prefix);
            if (!offer.BuyOnce &&
                ((index < 0 && memoCount == MaximumMemos) ||
                 (index >= 0 && (long)memos[index].Stack + offer.Tuple.Stack > int.MaxValue)))
            {
                ended = true;
                return false;
            }
            if (!work.TryPayNext(price))
            {
                ended = true;
                return false;
            }
            Units++;
            ChargedCopper = checked(ChargedCopper + price);
            BoughtCursor = BoughtCursor.Tuple.Type == 0
                ? born
                : BoughtCursor with { Tuple = BoughtCursor.Tuple with { Stack = BoughtCursor.Tuple.Stack + 1 } };
            work.SetCursor(BoughtCursor.Tuple);
            if (offer.BuyOnce)
                stock[slot] = offer.Tuple.Stack == 1 ? default : offer with { Tuple = offer.Tuple with { Stack = offer.Tuple.Stack - 1 } };
            else if (index >= 0)
                memos[index] = memos[index] with { Stack = memos[index].Stack + offer.Tuple.Stack };
            else
                memos[memoCount++] = new(offer.Tuple.Type, offer.Tuple.Prefix, offer.Tuple.Stack);
            return true;
        }

        public bool TryDetachCurrent(out ShopTransactionPlan1458? plan)
        {
            plan = null;
            if (Units == 0) return false;
            plan = new(work.DetachBought(ChargedCopper), stock.ToArray(), BoughtCursor,
                BoughtMemos.ToArray(), Units, -ChargedCopper);
            return true;
        }

        private int FindCurrentMemo(int type, int prefix)
        {
            int key = MemoKey(type);
            for (int index = 0; index < memoCount; index++)
                if (MemoKey(memos[index].Type) == key && memos[index].Prefix == prefix) return index;
            return -1;
        }
    }
}
