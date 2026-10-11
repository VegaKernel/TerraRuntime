using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct ShopWalletMutation1458(byte Wallet, short Slot,
    WorldItemPickupSlot1458 Before, WorldItemPickupSlot1458 After);
public sealed record ShopCursorPurchasePlan1458(ShopWalletPlan1458 Wallet,
    WorldItemPickupSlot1458 Stock, WorldItemPickupSlot1458 Cursor, int Units, int SellbackAdded);

public sealed record ShopWalletPlan1458(long Copper, WorldItemPickupSlot1458[] Inventory,
    WorldItemPickupSlot1458[] Banks, ShopWalletMutation1458[] Mutations);

/// <summary>Detached 1.4.5.8 Player wallet arithmetic. Runtime funding and shop intent remain caller-owned.</summary>
public static partial class VanillaShopWallet1458
{
    private const int BankCount = 4;
    private const int BankSlots = 40;
    private const int PaymentSlots = VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive;

    public static bool TryResolvePrices(int storeValue, float adjustment, bool discount,
        bool buyOnce, bool shopItem, out long selling, out long buying)
        => TryResolvePrices(storeValue, adjustment, discount, buyOnce, shopItem, out selling, out buying, false);

    public static bool TryResolvePrices(int storeValue, float adjustment, bool discount,
        bool buyOnce, bool shopItem, out long selling, out long buying, bool windowsItemPrefixArithmetic)
    {
        selling = buying = 0;
        if (storeValue < 0 || !float.IsFinite(adjustment) || adjustment <= 0) return false;
        long sell = storeValue, buy = storeValue;
        if (discount)
        {
            if (!buyOnce)
                buy = windowsItemPrefixArithmetic
                    ? (int)((double)(float)buy * .8f)
                    : (int)((float)buy * .8f);
            if (shopItem) sell = buy;
        }
        // The original binaries have conv.r4 before the operation and conv.r8
        // before Round. Captured CLR4 x86 keeps the operation wider; preserve
        // the initial Single conversion, including integers above 2^24.
        double adjustedBuy = windowsItemPrefixArithmetic
            ? (buyOnce ? (double)(float)buy / adjustment : (double)(float)buy * adjustment)
            : (buyOnce ? (float)buy / adjustment : (float)buy * adjustment);
        double adjustedSell = windowsItemPrefixArithmetic
            ? (double)(float)sell / adjustment
            : (float)sell / adjustment;
        double roundedBuy = Math.Round(adjustedBuy);
        double roundedSell = Math.Round(adjustedSell);
        if (roundedBuy < 0 || roundedBuy > int.MaxValue || roundedSell < 0 || roundedSell > int.MaxValue) return false;
        buying = (int)roundedBuy;
        selling = (int)roundedSell;
        if (buyOnce) buying = storeValue != 0 ? Math.Max(1, buying / 5) : buying / 5;
        return true;
    }

    public static bool TryPlanPurchase(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks, long price, out ShopWalletPlan1458? plan)
    {
        plan = null;
        if (price < 0 || !TryCapture(inventory, banks, out var images)) return false;
        var coins = new List<(int Wallet, int Slot)>(PaymentSlots + BankCount * BankSlots);
        var empty = CreateEmptyLists();
        if (!TryPay(images, price, coins, empty)) return false;
        plan = MakePlan(inventory, banks, images, price);
        return true;
    }

    private static List<int>[] CreateEmptyLists()
    {
        var empty = new List<int>[BankCount + 1];
        for (int wallet = 0; wallet < empty.Length; wallet++)
            empty[wallet] = new(PaymentSlots + BankCount * BankSlots);
        return empty;
    }

    private static bool TryPay(WorldItemPickupSlot1458[][] images, long price,
        List<(int Wallet, int Slot)> coins, List<int>[] empty)
    {
        long available = 0;
        coins.Clear();
        for (int wallet = 0; wallet < images.Length; wallet++)
        {
            empty[wallet].Clear();
            int count = wallet == 0 ? PaymentSlots : BankSlots;
            for (int slot = 0; slot < count; slot++)
            {
                var item = images[wallet][slot];
                long unit = Unit(item.Type);
                if (unit > 0) { available += unit * item.Stack; coins.Add((wallet, slot)); }
                if (item.Type == 0) empty[wallet].Add(Encode(wallet, slot)); // gameplay-domain-literal-audit: allow raw-entity-type-decision - Zero validates canonical Air as an available wallet slot.
            }
        }
        foreach (var list in empty) list.Sort(CompareEncodedDescendingSlot);
        if (Math.Min(available, VanillaCoinFacts.MaximumCombinedPurchaseCopper) < price) return false;
        long remaining = price;
        // Each pass either pays value or breaks one denomination. The four bounded
        // wallets contain at most 214 canonical coin piles; no external callbacks occur.
        Span<int> preference = stackalloc int[5];
        int steps = 0;
        while (remaining > 0)
        {
            if (++steps > (PaymentSlots + BankCount * BankSlots) * 4 + 4) return false;
            for (int denomination = 3; denomination >= 0; denomination--)
            {
                long unit = Unit(VanillaCoinFacts.CopperCoin.Value + denomination);
                if (remaining < unit) continue;
                foreach (var position in coins)
                {
                    var item = images[position.Wallet][position.Slot];
                    if (item.Type != VanillaCoinFacts.CopperCoin.Value + denomination) continue;
                    int spent = (int)Math.Min(item.Stack, remaining / unit);
                    if (spent == 0) continue;
                    images[position.Wallet][position.Slot] = spent == item.Stack ? default : item with { Stack = item.Stack - spent };
                    remaining -= spent * unit;
                    if (spent == item.Stack) empty[0].Add(Encode(position.Wallet, position.Slot));
                }
            }
            if (remaining == 0) break;
            // Source fallback is contingent on the ordinary empty list, even when
            // another bank has space. Spent bank entries join that shared list.
            if (empty[0].Count == 0) return false;
            empty[0].Sort(CompareEncodedDescendingSlot);
            bool broken = false;
            for (int wallet = 0; wallet < images.Length && !broken; wallet++)
            {
                int preferenceCount = 0;
                for (int denomination = 3; denomination > 0; denomination--)
                    if (remaining >= Unit(VanillaCoinFacts.CopperCoin.Value + denomination - 1)) preference[preferenceCount++] = denomination;
                preference[preferenceCount++] = 2;
                preference[preferenceCount++] = 3;
                foreach (int denomination in preference[..preferenceCount])
                {
                    foreach (var position in coins)
                    {
                        var item = images[position.Wallet][position.Slot];
                        if (position.Wallet != wallet || item.Type != VanillaCoinFacts.CopperCoin.Value + denomination || item.Stack < 1) continue;
                        var destinationList = wallet > 0 && empty[wallet].Count > 0 ? empty[wallet] : empty[0];
                        images[wallet][position.Slot] = item.Stack == 1 ? default : item with { Stack = item.Stack - 1 };
                        if (item.Stack == 1) destinationList.Add(Encode(wallet, position.Slot));
                        int encoded = destinationList[0]; destinationList.RemoveAt(0);
                        int targetWallet = encoded / 64, targetSlot = encoded % 64;
                        images[targetWallet][targetSlot] = new(item.Type - 1, 100, 0, false);
                        coins.Add((targetWallet, targetSlot));
                        broken = true;
                        break;
                    }
                    if (broken) break;
                }
            }
            if (!broken) return false;
            foreach (var list in empty) list.Sort(CompareEncodedDescendingSlot);
        }
        return true;
    }

    public static bool TryPlanSale(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks, long selling, long buying,
        int offeredStack, int sellbackCount, out ShopWalletPlan1458? plan)
    {
        plan = null;
        if (selling <= 0 || buying < 0 || offeredStack < 1 || sellbackCount < 0 ||
            !TryCapture(inventory, banks, out var images)) return false;
        long perItem = Math.Max(1, selling / 5);
        long grant;
        try { grant = checked(perItem * offeredStack + (buying - perItem) * Math.Min(sellbackCount, offeredStack)); }
        catch (OverflowException) { return false; }
        if (grant < 0) return false;
        if (!TryDepositSale(images[0], grant)) return false;
        plan = MakePlan(inventory, banks, images, grant);
        return true;
    }

    private static bool TryDepositSale(WorldItemPickupSlot1458[] items, long grant)
    {
        long remaining = grant;
        static void Convert(WorldItemPickupSlot1458[] items, int slot)
        {
            var item = items[slot];
            if (item.Stack != 100 || item.Type < VanillaCoinFacts.CopperCoin.Value || item.Type >= VanillaCoinFacts.PlatinumCoin.Value) return;
            items[slot] = new(item.Type + 1, 1, 0, false);
            for (int destination = 0; destination < PaymentSlots; destination++)
            {
                var next = items[destination];
                if (destination == slot || next.Type != item.Type + 1 || next.Stack >= Maximum(next.Type)) continue;
                items[destination] = next with { Stack = next.Stack + 1 };
                items[slot] = default;
                Convert(items, destination);
                break;
            }
        }
        for (int denomination = 3; denomination >= 0; denomination--)
        {
            int type = VanillaCoinFacts.CopperCoin.Value + denomination;
            long unit = Unit(type);
            while (remaining >= unit)
            {
                int firstEmpty = -1;
                for (int slot = PaymentSlots - 1; slot >= 0; slot--)
                {
                    if (firstEmpty < 0 && items[slot].Type == 0) firstEmpty = slot;
                    while (items[slot].Type == type && items[slot].Stack < Maximum(type) && remaining >= unit)
                    {
                        var item = items[slot];
                        int amount = (int)Math.Min(remaining / unit, Maximum(type) - item.Stack);
                        items[slot] = item with { Stack = item.Stack + amount };
                        remaining -= amount * unit;
                        Convert(items, slot);
                        if (items[slot].Type == 0 && firstEmpty < 0) firstEmpty = slot;
                    }
                }
                if (remaining < unit) break;
                if (firstEmpty < 0) return false;
                items[firstEmpty] = new(type, 1, 0, false);
                remaining -= unit;
            }
        }
        return true;
    }

    /// <summary>Source HandleShopSlot cursor birth/merge and finite buyOnce decrement; UI and RNG prefix ownership remain outside this detached wallet.</summary>
    public static bool TryPlanCursorPurchase(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks, in WorldItemPickupSlot1458 stock,
        bool buyOnce, long price, int requestedUnits, out ShopCursorPurchasePlan1458? plan,
        int itemAnimation = 0, int itemTime = 0, bool rightClick = false, int stackSplit = 0)
    {
        plan = null;
        if (itemAnimation != 0 || itemTime < 0 || (!rightClick && itemTime != 0) || stackSplit > 1 ||
            !Canonical(in stock) || stock.Type == 0 || price < 0 || requestedUnits < 1 ||
            requestedUnits > VanillaCoinFacts.PlatinumMaximumStack ||
            !TryCapture(inventory, banks, out var images) ||
            !VanillaWorldItemAllocationCatalog1458.TryGet(stock.Type, out var definition)) return false;
        var cursor = inventory[VanillaPlayerItemSlotCatalog.InventoryMouseItem];
        if (cursor.Type != 0 && (cursor.Type != stock.Type || cursor.Prefix != stock.Prefix)) return false;
        var rollback = images.Select(image => new WorldItemPickupSlot1458[image.Length]).ToArray();
        var coins = new List<(int Wallet, int Slot)>(PaymentSlots + BankCount * BankSlots);
        var empty = CreateEmptyLists();
        var nextStock = stock;
        int units = 0, sellbackAdded = 0;
        long charged = 0;
        for (int attempt = 0; attempt < requestedUnits; attempt++)
        {
            if (cursor.Stack >= definition.MaximumStack || nextStock.Stack == 0) break;
            for (int wallet = 0; wallet < images.Length; wallet++) images[wallet].CopyTo(rollback[wallet], 0);
            if (!TryPay(images, price, coins, empty))
            {
                // Original BuyItem rollback restores the pre-unit wallet, while
                // previously purchased units in the same UI batch remain adopted.
                for (int wallet = 0; wallet < images.Length; wallet++) rollback[wallet].CopyTo(images[wallet], 0);
                break;
            }
            if (cursor.Type == 0) cursor = new(stock.Type, 0, stock.Prefix, false);
            cursor = cursor with { Stack = cursor.Stack + 1 };
            if (!buyOnce) sellbackAdded = checked(sellbackAdded + nextStock.Stack);
            else nextStock = nextStock.Stack == 1 ? default : nextStock with { Stack = nextStock.Stack - 1 };
            units++;
            charged = checked(charged + price);
        }
        if (units == 0) return false;
        images[0][VanillaPlayerItemSlotCatalog.InventoryMouseItem] = cursor;
        plan = new(MakePlan(inventory, banks, images, charged), nextStock, cursor, units, sellbackAdded);
        return true;
    }

    /// <summary>Cursor sale caller's complete debit after successful wallet payout, including source zero-value clearing.</summary>
    public static bool TryPlanCursorSale(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks, long selling, long buying,
        int sourceItemValue, int sellbackCount, out ShopWalletPlan1458? plan)
    {
        plan = null;
        if (!TryCapture(inventory, banks, out var images)) return false;
        var cursor = inventory[VanillaPlayerItemSlotCatalog.InventoryMouseItem];
        if (cursor.Type == 0 || Unit(cursor.Type) != 0 || sourceItemValue < 0) return false;
        if (TryPlanSale(inventory, banks, selling, buying, cursor.Stack, sellbackCount, out var payment))
        {
            images[0] = payment!.Inventory;
            for (int bank = 0; bank < BankCount; bank++) images[bank + 1] = payment.Banks.AsSpan(bank * BankSlots, BankSlots).ToArray();
            images[0][VanillaPlayerItemSlotCatalog.InventoryMouseItem] = default;
            plan = MakePlan(inventory, banks, images, payment.Copper);
            return true;
        }
        if (sourceItemValue != 0) return false;
        images[0][VanillaPlayerItemSlotCatalog.InventoryMouseItem] = default;
        plan = MakePlan(inventory, banks, images, 0);
        return true;
    }

    private static bool TryCapture(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks, out WorldItemPickupSlot1458[][] images)
    {
        images = [];
        if (inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount || banks.Length != BankCount * BankSlots) return false;
        foreach (var item in inventory) if (!Canonical(item)) return false;
        foreach (var item in banks) if (!Canonical(item)) return false;
        images = new WorldItemPickupSlot1458[BankCount + 1][];
        images[0] = inventory.ToArray();
        for (int bank = 0; bank < BankCount; bank++) images[bank + 1] = banks.Slice(bank * BankSlots, BankSlots).ToArray();
        return true;
    }
    private static bool Canonical(in WorldItemPickupSlot1458 item) => item.Type == 0 // gameplay-domain-literal-audit: allow raw-entity-type-decision - Zero validates canonical Air, not a content-specific rule.
        ? item == default
        : item.Stack > 0 && (uint)item.Prefix <= 97 && VanillaWorldItemAllocationCatalog1458.TryGet(item.Type, out var facts) &&
            (VanillaCoinFacts.TryGetValue(new ItemTypeId(item.Type), out _)
                ? VanillaCoinFacts.IsValidRetainedStack(new ItemTypeId(item.Type), item.Stack)
                : item.Stack <= facts.MaximumStack);
    private static int Maximum(int type) => type == VanillaCoinFacts.PlatinumCoin.Value
        ? VanillaCoinFacts.PlatinumMaximumStack : VanillaCoinFacts.LowerDenominationMaximumStack;
    private static long Unit(int type) => type switch { 71 => 1, 72 => 100, 73 => 10000, 74 => 1000000, _ => 0 };
    private static int Encode(int wallet, int slot) => wallet * 64 + slot;
    private static int CompareEncodedDescendingSlot(int left, int right) => (right % 64).CompareTo(left % 64);
    private static ShopWalletPlan1458 MakePlan(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> banks, WorldItemPickupSlot1458[][] images, long copper)
    {
        var mutations = new List<ShopWalletMutation1458>();
        for (int wallet = 0; wallet < images.Length; wallet++)
            for (short slot = 0; slot < images[wallet].Length; slot++)
            {
                var before = wallet == 0 ? inventory[slot] : banks[(wallet - 1) * BankSlots + slot];
                if (before != images[wallet][slot]) mutations.Add(new((byte)wallet, slot, before, images[wallet][slot]));
            }
        return new(copper, images[0], images.Skip(1).SelectMany(x => x).ToArray(), mutations.ToArray());
    }
}
