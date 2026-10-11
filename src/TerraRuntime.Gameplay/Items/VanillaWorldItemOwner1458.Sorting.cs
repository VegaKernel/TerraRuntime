using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public static partial class VanillaWorldItemOwner1458
{
    private const int CoinGunSortExemption = 905;
    private const int LastMaterialsSortLayer = 47;
    private const int LastTrashSortLayer = 51;

    /// <summary>Detached official sorting. Locale and class ordering are finite source
    /// contexts; neither reports nor this plan establish their runtime ownership.
    /// Overflow records the source QuickSpawn offers; adopting those world births
    /// requires a separate joint runtime inventory/world transaction.</summary>
    public static bool TryPlanSort(ReadOnlySpan<WorldItemPickupSlot1458> slots,
        bool mainInventory, byte locale, byte classOrder, bool inventoryLocked,
        bool windowsArithmetic, VanillaItemPrefixWorld1458? world,
        out WorldItemSortPlan1458? plan)
    {
        plan = null;
        int expected = mainInventory ? VanillaPlayerItemSlotCatalog.InventoryCount : BankInventoryCount;
        if (slots.Length != expected || locale >= 12 || !TrySortClassRanks(classOrder, out var classRanks))
            return false;
        foreach (var item in slots)
        {
            if (!IsCanonicalStorageSlot(in item)) return false;
            if (item.Type != 0 && PrefixWorldRecords1458.HasVariant(item.Type) && // gameplay-domain-literal-audit: allow raw-entity-type-decision - Zero is canonical Air; only retained nonempty items have variant contexts.
                (world is not { } known || PrefixWorldRecords1458.IsVariantActive(item.Type, in known)))
                return false;
        }
        var image = slots.ToArray();
        var steps = new List<BankStorageStep1458>();
        WorldItemPickupSlot1458[] overflow = [];
        int offset = mainInventory ? 0 : VanillaPlayerItemSlotCatalog.InventoryCount;
        if (mainInventory && inventoryLocked)
        {
            plan = new(image, [], new([], [], []));
            return true;
        }
        if (mainInventory)
        {
            bool coinGun = false;
            for (int slot = 0; slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; slot++)
                coinGun |= image[slot].Type == CoinGunSortExemption;
            if (!coinGun && !TrySortCoins(image, steps, out overflow)) return false;
            SortAmmoImage(image, steps);
        }
        int first = mainInventory ? HotbarEnd : 0;
        int end = mainInventory ? VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive : image.Length;
        var mutable = new List<short>(end - first);
        var active = new List<short>(end - first);
        for (short slot = (short)first; slot < end; slot++)
        {
            if (image[slot].Favorite) continue;
            mutable.Add(slot);
            if (image[slot].Type != 0) active.Add(slot);
        }
        // Only partial stacks can donate during the source's preliminary consolidation.
        for (int target = 0; target < active.Count; target++)
        {
            short destination = active[target];
            if (image[destination].Stack >= MaximumBankStack(image[destination])) continue;
            for (int donor = target + 1; donor < active.Count; donor++)
            {
                short source = active[donor];
                if (!CanStackBankStorage(image[source], image[destination]) ||
                    image[source].Stack == MaximumBankStack(image[source])) continue;
                int amount = Math.Min(image[source].Stack,
                    MaximumBankStack(image[destination]) - image[destination].Stack);
                SortTransfer(image, source, destination, amount, offset, steps);
                if (image[source].Type == 0)
                {
                    active.RemoveAt(donor);
                    target--;
                    break;
                }
                if (image[destination].Stack == MaximumBankStack(image[destination])) break;
            }
        }
        var effectiveValues = new Dictionary<int, int>();
        foreach (short source in active)
        {
            var item = image[source];
            int layer = SortLayers[item.Type];
            if (layer is LastMaterialsSortLayer or LastTrashSortLayer)
            {
                if (!VanillaItemStoreValue1458.TryGet(new ItemTypeId(item.Type), new PrefixId(item.Prefix),
                    out int value, windowsArithmetic, world)) return false;
                effectiveValues[source] = value;
            }
        }
        active.Sort((left, right) => CompareSortSlots(image, left, right, locale, classRanks, effectiveValues));
        var sources = active.Concat(mutable.Where(slot => image[slot].Type == 0)).ToArray();
        var before = sources.Select(slot => image[slot]).ToArray();
        var destinations = mutable.ToArray();
        for (int i = 0; i < destinations.Length; i++) image[destinations[i]] = before[i];
        var permutation = new WorldItemSortPermutation1458(
            sources.Select(slot => (short)(slot + offset)).ToArray(),
            destinations.Select(slot => (short)(slot + offset)).ToArray(), before);
        plan = new(image, steps.ToArray(), permutation, overflow);
        return true;
    }

    private static bool TrySortClassRanks(byte order, out int[] ranks)
    {
        ranks = new int[4];
        int used = 0;
        for (int position = 0; position < 4; position++)
        {
            int type = (order >> (2 * position)) & 3;
            if ((used & (1 << type)) != 0) return false;
            used |= 1 << type;
            ranks[type] = position;
        }
        return true;
    }

    private static int CompareSortSlots(WorldItemPickupSlot1458[] image, short left, short right,
        byte locale, int[] classRanks, Dictionary<int, int> values)
    {
        var a = image[left];
        var b = image[right];
        int layer = SortLayers[a.Type];
        int otherLayer = SortLayers[b.Type];
        int result = (layer < 4 ? classRanks[layer] : layer).CompareTo(
            otherLayer < 4 ? classRanks[otherLayer] : otherLayer);
        if (result != 0) return result;
        if (layer is LastMaterialsSortLayer or LastTrashSortLayer)
        {
            if (layer == LastMaterialsSortLayer)
                result = SortOriginalRarities[b.Type].CompareTo(SortOriginalRarities[a.Type]);
            if (result == 0) result = values[right].CompareTo(values[left]);
        }
        else result = SortRanks(locale)[a.Type].CompareTo(SortRanks(locale)[b.Type]);
        if (result == 0 && ((SortStackLayers >> layer) & 1) != 0)
            result = b.Stack.CompareTo(a.Stack);
        return result != 0 ? result : left.CompareTo(right);
    }

    private static void SortTransfer(WorldItemPickupSlot1458[] image, short source,
        short destination, int amount, int offset, List<BankStorageStep1458> steps)
    {
        var beforeSource = image[source];
        var beforeDestination = image[destination];
        steps.Add(new((short)(source + offset), (short)(destination + offset),
            beforeSource, beforeDestination, amount));
        image[destination] = beforeDestination.Type == 0 ? beforeSource with { Stack = amount } :
            beforeDestination with { Stack = beforeDestination.Stack + amount };
        image[source] = amount == beforeSource.Stack ? default : beforeSource with { Stack = beforeSource.Stack - amount };
    }

    private static bool TrySortCoins(WorldItemPickupSlot1458[] image, List<BankStorageStep1458> steps,
        out WorldItemPickupSlot1458[] overflow)
    {
        overflow = [];
        var before = image.ToArray();
        long total = 0;
        int occupied = 0;
        for (int slot = 0; slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; slot++)
            if (VanillaCoinFacts.TryGetValue(new ItemTypeId(image[slot].Type), out long unit))
            {
                total += unit * image[slot].Stack;
                occupied++;
            }
        if (total == 0) return true;
        var counts = new int[4];
        long remaining = total;
        for (int denomination = 3; denomination >= 0; denomination--)
        {
            long unit = denomination switch { 3 => 1_000_000, 2 => 10_000, 1 => 100, _ => 1 };
            counts[denomination] = checked((int)(remaining / unit));
            remaining %= unit;
        }
        int required = counts[0] > 0 ? 1 : 0;
        required += counts[1] > 0 ? 1 : 0;
        required += counts[2] > 0 ? 1 : 0;
        required += counts[3] == 0 ? 0 : (counts[3] - 1) / VanillaCoinFacts.PlatinumMaximumStack;
        if (occupied < required) return true;
        for (int slot = 0; slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; slot++)
            if (IsBankCoin(in image[slot])) image[slot] = default;
        var order = new List<short>();
        var remainingOffers = new List<WorldItemPickupSlot1458>();
        for (int denomination = 3; denomination >= 0; denomination--)
            while (counts[denomination] > 0)
            {
                int destination = -1;
                for (int slot = VanillaPlayerItemSlotCatalog.CoinSlotStart;
                    slot < VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive && destination < 0; slot++)
                    if (image[slot].Type == 0) destination = slot; // gameplay-domain-literal-audit: allow raw-entity-type-decision - Canonical Air is an empty refill destination.
                for (int slot = 0; slot < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive && destination < 0; slot++)
                    if (image[slot].Type == 0) destination = slot; // gameplay-domain-literal-audit: allow raw-entity-type-decision - Canonical Air is an empty refill destination.
                if (destination < 0)
                {
                    // Original QuickSpawn offers each entire remaining denomination.
                    // This may exceed a canonical stack or the packet's signed short;
                    // the numeric source plan does not authorize a runtime wire birth.
                    remainingOffers.Add(new(VanillaCoinFacts.CopperCoin.Value + denomination,
                        counts[denomination], VanillaPrefixIds.NoneValue, false));
                    counts[denomination] = 0;
                    continue;
                }
                int amount = denomination == 3 ? Math.Min(counts[denomination], VanillaCoinFacts.PlatinumMaximumStack) : counts[denomination];
                image[destination] = new(VanillaCoinFacts.CopperCoin.Value + denomination, amount, VanillaPrefixIds.NoneValue, false);
                order.Add((short)destination);
                counts[denomination] -= amount;
            }
        overflow = remainingOffers.ToArray();
        AddBankCoinStep(before, image, [], order.ToArray(), steps);
        if (overflow.Length > 0)
        {
            var last = steps[^1];
            if (last.Coins is not { } rewrite) return false;
            steps[^1] = last with { Coins = rewrite with { Overflow = overflow } };
        }
        return true;
    }

    private static void SortAmmoImage(WorldItemPickupSlot1458[] image, List<BankStorageStep1458> steps)
    {
        for (short destination = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
            destination < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; destination++)
        {
            var item = image[destination];
            if (item.Type == 0 || !VanillaWorldItemPickupCatalog1458.TryGet(item.Type, out var facts) || // gameplay-domain-literal-audit: allow raw-entity-type-decision - Zero tests canonical Air while identifying empty or occupied storage slots.
                !facts.Ammo || item.Stack >= MaximumBankStack(item)) continue;
            RefillSortAmmo(image, destination, item.Favorite ? VanillaPlayerItemSlotCatalog.AmmoSlotStart : destination + 1,
                VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive, steps);
        }
        for (short source = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
            source < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; source++)
            if (image[source].Type != 0 && !image[source].Favorite)
                for (short destination = VanillaPlayerItemSlotCatalog.AmmoSlotStart; destination < source; destination++)
                    if (image[destination].Type == 0)
                    {
                        SortTransfer(image, source, destination, image[source].Stack, 0, steps);
                        break;
                    }
        var types = new HashSet<int>();
        var empty = new List<short>();
        for (short slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
            slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; slot++)
        {
            if (image[slot].Type == 0) empty.Add(slot);
            else if (VanillaWorldItemPickupCatalog1458.TryGet(image[slot].Type, out var facts) && facts.Ammo)
            {
                types.Add(image[slot].Type);
                RefillSortAmmo(image, slot, 0, VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive, steps);
            }
        }
        for (int pass = 0; pass < 2; pass++)
            for (short source = 0; source < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive && empty.Count > 0; source++)
            {
                var item = image[source];
                if (item.Type == 0 || item.Favorite || !VanillaWorldItemPickupCatalog1458.TryGet(item.Type, out var facts) || // gameplay-domain-literal-audit: allow raw-entity-type-decision - Zero tests canonical Air while identifying empty or occupied storage slots.
                    !facts.EmptyAmmo || (pass == 0 ? !types.Contains(item.Type) : !facts.FitsAmmoSlot)) continue;
                short destination = empty[0];
                empty.RemoveAt(0);
                SortTransfer(image, source, destination, item.Stack, 0, steps);
                RefillSortAmmo(image, destination, 0, VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive, steps);
            }
    }

    private static void RefillSortAmmo(WorldItemPickupSlot1458[] image, short destination,
        int first, int end, List<BankStorageStep1458> steps)
    {
        for (short source = (short)first; source < end && image[destination].Stack < MaximumBankStack(image[destination]); source++)
            if (source != destination && image[source].Type == image[destination].Type && !image[source].Favorite)
                SortTransfer(image, source, destination, Math.Min(image[source].Stack,
                    MaximumBankStack(image[destination]) - image[destination].Stack), 0, steps);
    }
}
