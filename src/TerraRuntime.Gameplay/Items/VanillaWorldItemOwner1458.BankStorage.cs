using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public enum BankBulkOperation1458 : byte
{
    LootAll,
    DepositAll,
    QuickStack,
    ShiftIntoBank,
    ShiftOutOfBank
}

/// <summary>One detached coin rewrite; slots use main0..58 and bank59..98.</summary>
public sealed record BankCoinNormalization1458(short[] Slots,
    WorldItemPickupSlot1458[] Before, WorldItemPickupSlot1458[] After,
    short[] DestinationOrder);

/// <summary>Physical transfer order, or a coin rewrite when Coins is non-null. No trust is assigned.</summary>
public readonly record struct BankStorageStep1458(short Source, short Destination,
    WorldItemPickupSlot1458 SourceBefore, WorldItemPickupSlot1458 DestinationBefore,
    int Amount, BankCoinNormalization1458? Coins = null);

public sealed record BankBulkStoragePlan1458(WorldItemPickupSlot1458[] Inventory,
    WorldItemPickupSlot1458[] Bank, BankStorageStep1458[] Steps);

public static partial class VanillaWorldItemOwner1458
{
    private const int BankInventoryCount = VanillaPlayerItemSlotCatalog.Bank4Count;
    private const int HotbarEnd = 10;
    private const int MoneyTrough = 3213;
    private const int OpenVoidBag = 4131;
    private const int ClosedVoidBag = 5325;

    /// <summary>
    /// Detached 1.4.5.8 ChestUI bank storage. Bulk and configured transfer overrides
    /// run before ordinary cursor clock gates. Reports do not identify a UI operation
    /// or authorize funding; the caller must resolve the complete conserved journal.
    /// </summary>
    public static bool TryPlanBankBulk(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> bank, int bankIndex,
        BankBulkOperation1458 operation, short sourceSlot, VanillaItemPrefixWorld1458? world,
        out BankBulkStoragePlan1458? plan)
    {
        plan = null;
        if (inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            bank.Length != BankInventoryCount || (uint)bankIndex >= 4 ||
            operation > BankBulkOperation1458.ShiftOutOfBank)
            return false;
        foreach (ref readonly var item in inventory)
            if (!IsCanonicalStorageSlot(in item)) return false;
        foreach (ref readonly var item in bank)
            if (!IsCanonicalStorageSlot(in item)) return false;

        var main = inventory.ToArray();
        var storage = bank.ToArray();
        var steps = new List<BankStorageStep1458>();
        if (operation is BankBulkOperation1458.DepositAll or BankBulkOperation1458.QuickStack)
            NormalizeBankCoins(main, storage, steps);

        switch (operation)
        {
            case BankBulkOperation1458.LootAll:
                for (short slot = 0; slot < BankInventoryCount; slot++)
                    if (storage[slot].Type != 0 && !(bankIndex == 3 && storage[slot].Favorite) &&
                        !LootBankSlot(main, storage, slot, world, steps)) return false;
                break;
            case BankBulkOperation1458.DepositAll:
                for (int slot = VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive - 1;
                    slot >= HotbarEnd; slot--)
                    if (main[slot].Type != 0 && !main[slot].Favorite)
                        DepositBankSlot(main, storage, (short)slot, steps);
                break;
            case BankBulkOperation1458.QuickStack:
                QuickStackBank(main, storage, steps);
                break;
            case BankBulkOperation1458.ShiftIntoBank:
                if ((uint)sourceSlot >= VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount ||
                    main[sourceSlot].Type == 0) return false;
                var selected = main[sourceSlot];
                // ChestUI.IsBlockedFromTransferIntoChest protects the favorited portable opener.
                if (!selected.Favorite || !((bankIndex == 0 && selected.Type == MoneyTrough) ||
                    (bankIndex == 3 && selected.Type is OpenVoidBag or ClosedVoidBag)))
                    DepositBankSlot(main, storage, sourceSlot, steps);
                break;
            case BankBulkOperation1458.ShiftOutOfBank:
                if ((uint)sourceSlot >= BankInventoryCount || storage[sourceSlot].Type == 0 ||
                    !LootBankSlot(main, storage, sourceSlot, world, steps)) return false;
                break;
        }
        if (!ConservesBankStorage(inventory, bank, main, storage)) return false;
        plan = new(main, storage, steps.ToArray());
        return true;
    }

    private static bool LootBankSlot(WorldItemPickupSlot1458[] main,
        WorldItemPickupSlot1458[] bank, short slot, VanillaItemPrefixWorld1458? world,
        List<BankStorageStep1458> steps)
    {
        var source = bank[slot];
        if (!TryPlanStorage(in source, main, false, world, out var storage) || storage is null)
            return false;
        if (IsBankCoin(in source))
        {
            var before = main.Concat(bank).ToArray();
            foreach (var mutation in storage.Mutations) main[mutation.Slot] = mutation.After;
            bank[slot] = storage.Residual;
            var order = storage.Mutations.Select(x => x.Slot)
                .Append((short)(VanillaPlayerItemSlotCatalog.InventoryCount + slot)).ToArray();
            AddBankCoinStep(before, main, bank, order, steps);
        }
        else
        {
            foreach (var mutation in storage.Mutations)
            {
                int amount = mutation.After.Stack - mutation.Before.Stack;
                if (mutation.Before.Type == 0) amount = mutation.After.Stack;
                TransferBankStorage(main, bank,
                    (short)(VanillaPlayerItemSlotCatalog.InventoryCount + slot), mutation.Slot,
                    amount, steps, mutation.After);
            }
        }
        return bank[slot] == storage.Residual;
    }

    private static void DepositBankSlot(WorldItemPickupSlot1458[] main,
        WorldItemPickupSlot1458[] bank, short source, List<BankStorageStep1458> steps)
    {
        if (MaximumBankStack(main[source]) > 1)
            for (short destination = 0; destination < BankInventoryCount && main[source].Type != 0;
                destination++)
                if (CanStackBankStorage(main[source], bank[destination]))
                {
                    int amount = Math.Min(main[source].Stack,
                        MaximumBankStack(bank[destination]) - bank[destination].Stack);
                    if (amount > 0) TransferBankStorage(main, bank, source,
                        (short)(VanillaPlayerItemSlotCatalog.InventoryCount + destination), amount, steps);
                }
        if (main[source].Type == 0) return;
        for (short destination = 0; destination < BankInventoryCount; destination++)
            if (bank[destination].Type == 0)
            {
                TransferBankStorage(main, bank, source,
                    (short)(VanillaPlayerItemSlotCatalog.InventoryCount + destination), main[source].Stack, steps);
                break;
            }
    }

    private static void QuickStackBank(WorldItemPickupSlot1458[] main,
        WorldItemPickupSlot1458[] bank, List<BankStorageStep1458> steps)
    {
        var existing = new List<short>(BankInventoryCount);
        var empty = new List<short>(BankInventoryCount);
        var types = new HashSet<int>();
        for (short slot = 0; slot < BankInventoryCount; slot++)
        {
            if (bank[slot].Type == 0) empty.Add(slot);
            else if (!IsBankCoin(in bank[slot])) { existing.Add(slot); types.Add(bank[slot].Type); }
        }
        var eligible = new List<short>(VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive - HotbarEnd);
        for (short slot = HotbarEnd; slot < VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive; slot++)
            if (!main[slot].Favorite && types.Contains(main[slot].Type)) eligible.Add(slot);
        foreach (short destination in existing)
            foreach (short source in eligible)
                if (main[source].Type != 0 && CanStackBankStorage(main[source], bank[destination]))
                {
                    int room = MaximumBankStack(bank[destination]) - bank[destination].Stack;
                    if (room == 0) break;
                    TransferBankStorage(main, bank, source,
                        (short)(VanillaPlayerItemSlotCatalog.InventoryCount + destination),
                        Math.Min(main[source].Stack, room), steps);
                }
        foreach (short destination in empty)
            foreach (short source in eligible)
            {
                if (main[source].Type == 0 || (bank[destination].Type != 0 &&
                    !CanStackBankStorage(main[source], bank[destination]))) continue;
                int room = bank[destination].Type == 0 ? MaximumBankStack(main[source]) :
                    MaximumBankStack(bank[destination]) - bank[destination].Stack;
                if (room == 0) break;
                TransferBankStorage(main, bank, source,
                    (short)(VanillaPlayerItemSlotCatalog.InventoryCount + destination),
                    Math.Min(main[source].Stack, room), steps);
            }
    }

    private static void TransferBankStorage(WorldItemPickupSlot1458[] main,
        WorldItemPickupSlot1458[] bank, short source, short destination, int amount,
        List<BankStorageStep1458> steps, WorldItemPickupSlot1458? exactAfter = null)
    {
        var beforeSource = ReadBankStorage(main, bank, source);
        var beforeDestination = ReadBankStorage(main, bank, destination);
        var afterDestination = exactAfter ?? (beforeDestination.Type == 0
            ? beforeSource with { Stack = amount }
            : beforeDestination with { Stack = beforeDestination.Stack + amount });
        int residual = beforeSource.Stack - amount;
        WriteBankStorage(main, bank, source, residual == 0 ? default : beforeSource with { Stack = residual });
        WriteBankStorage(main, bank, destination, afterDestination);
        steps.Add(new(source, destination, beforeSource, beforeDestination, amount));
    }

    private static WorldItemPickupSlot1458 ReadBankStorage(WorldItemPickupSlot1458[] main,
        WorldItemPickupSlot1458[] bank, int slot) => slot < main.Length ? main[slot] : bank[slot - main.Length];

    private static void WriteBankStorage(WorldItemPickupSlot1458[] main,
        WorldItemPickupSlot1458[] bank, int slot, WorldItemPickupSlot1458 item)
    {
        if (slot < main.Length) main[slot] = item;
        else bank[slot - main.Length] = item;
    }

    private static bool CanStackBankStorage(WorldItemPickupSlot1458 first,
        WorldItemPickupSlot1458 second) => first.Type != 0 && first.Type == second.Type && first.Prefix == second.Prefix;

    private static int MaximumBankStack(WorldItemPickupSlot1458 item)
    {
        _ = VanillaWorldItemAllocationCatalog1458.TryGet(item.Type, out var facts);
        return facts.MaximumStack;
    }

    private static bool IsBankCoin(in WorldItemPickupSlot1458 item) =>
        VanillaCoinFacts.TryGetValue(new ItemTypeId(item.Type), out _);

    private static void NormalizeBankCoins(WorldItemPickupSlot1458[] main,
        WorldItemPickupSlot1458[] bank, List<BankStorageStep1458> steps)
    {
        // ChestUI.MoveCoins is gated by an existing bank coin. It retains old coin-slot
        // denomination priority before filling other empties, then returns overflow to
        // the original nonfavorite main coin slots. Cursor58 never participates.
        var oldCoins = new List<(short Slot, int Denomination)>();
        var empty = new List<short>();
        var counts = new long[4];
        var before = main.Concat(bank).ToArray();
        for (short slot = 0; slot < BankInventoryCount; slot++)
        {
            if (bank[slot].Type == 0) empty.Add(slot);
            else if (IsBankCoin(in bank[slot]))
            {
                int denomination = bank[slot].Type - VanillaCoinFacts.CopperCoin.Value;
                counts[denomination] += bank[slot].Stack;
                oldCoins.Add((slot, denomination));
                empty.Add(slot);
                bank[slot] = default;
            }
        }
        if (oldCoins.Count == 0) return;
        var mainCoins = new List<short>();
        for (short slot = 0; slot < VanillaPlayerItemSlotCatalog.InventoryCount; slot++)
            if (slot != VanillaPlayerItemSlotCatalog.InventoryMouseItem && !main[slot].Favorite && IsBankCoin(in main[slot]))
            {
                counts[main[slot].Type - VanillaCoinFacts.CopperCoin.Value] += main[slot].Stack;
                mainCoins.Add(slot);
                main[slot] = default;
            }
        for (int denomination = 0; denomination < 3; denomination++)
        {
            counts[denomination + 1] += counts[denomination] / VanillaCoinFacts.LowerDenominationMaximumStack;
            counts[denomination] %= VanillaCoinFacts.LowerDenominationMaximumStack;
        }
        var order = new List<short>();
        void Fill(short slot, int denomination)
        {
            if (counts[denomination] == 0) return;
            int maximum = denomination == 3 ? VanillaCoinFacts.PlatinumMaximumStack : VanillaCoinFacts.LowerDenominationMaximumStack;
            int amount = (int)Math.Min(counts[denomination], maximum);
            WriteBankStorage(main, bank, slot, new(VanillaCoinFacts.CopperCoin.Value + denomination,
                amount, VanillaPrefixIds.NoneValue, false));
            counts[denomination] -= amount;
            order.Add(slot);
        }
        foreach (var old in oldCoins)
            if (counts[old.Denomination] > 0)
            {
                Fill((short)(main.Length + old.Slot), old.Denomination);
                empty.Remove(old.Slot);
            }
        foreach (var old in oldCoins)
            if (bank[old.Slot].Type == 0)
            {
                for (int denomination = 3; denomination >= 0; denomination--)
                    if (counts[denomination] > 0) { Fill((short)(main.Length + old.Slot), denomination); break; }
                empty.Remove(old.Slot);
            }
        foreach (short slot in empty)
            for (int denomination = 3; denomination >= 0; denomination--)
                if (counts[denomination] > 0) { Fill((short)(main.Length + slot), denomination); break; }
        int mainIndex = 0;
        for (int denomination = 3; denomination >= 0; denomination--)
            while (counts[denomination] > 0 && mainIndex < mainCoins.Count)
                Fill(mainCoins[mainIndex++], denomination);
        AddBankCoinStep(before, main, bank, order.ToArray(), steps);
    }

    private static void AddBankCoinStep(WorldItemPickupSlot1458[] before,
        WorldItemPickupSlot1458[] main, WorldItemPickupSlot1458[] bank, short[] order,
        List<BankStorageStep1458> steps)
    {
        var after = main.Concat(bank).ToArray();
        var slots = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i] ||
            order.Contains((short)i)).Select(i => (short)i).ToArray();
        var coins = new BankCoinNormalization1458(slots, slots.Select(i => before[i]).ToArray(),
            slots.Select(i => after[i]).ToArray(), order);
        steps.Add(new(-1, -1, default, default, 0, coins));
    }

    private static bool ConservesBankStorage(ReadOnlySpan<WorldItemPickupSlot1458> beforeMain,
        ReadOnlySpan<WorldItemPickupSlot1458> beforeBank, ReadOnlySpan<WorldItemPickupSlot1458> afterMain,
        ReadOnlySpan<WorldItemPickupSlot1458> afterBank)
    {
        var quantities = new Dictionary<(int Type, int Prefix), long>();
        long coins = 0;
        void Count(WorldItemPickupSlot1458 item, int direction)
        {
            if (item.Type == 0) return; // gameplay-domain-literal-audit: allow raw-entity-type-decision - Canonical Air carries no storage quantity or coin value.
            if (VanillaCoinFacts.TryGetValue(new ItemTypeId(item.Type), out long unit)) coins += direction * unit * item.Stack;
            else { var key = (item.Type, item.Prefix); quantities.TryGetValue(key, out long value); quantities[key] = value + direction * item.Stack; }
        }
        foreach (var item in beforeMain) Count(item, 1);
        foreach (var item in beforeBank) Count(item, 1);
        foreach (var item in afterMain) Count(item, -1);
        foreach (var item in afterBank) Count(item, -1);
        return coins == 0 && quantities.Values.All(x => x == 0);
    }
}
