using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    internal BankChainWork? TryCreateShopBankChainWork(ConnectionHandle connection,
        ShopJournalCapture capture, RuntimePlayerInventoryItem[] reports, bool[] seen,
        int animation, int itemTime)
        => TryCreateShopBankChainWork(connection, capture, reports, seen, animation, itemTime, false);

    internal BankChainWork? TryCreateShopBankChainWork(ConnectionHandle connection,
        ShopJournalCapture capture, RuntimePlayerInventoryItem[] reports, bool[] seen,
        int animation, int itemTime, bool allowMainOnly)
    {
        if (!capture.IsCurrent || animation != 0 || reports.Length != 219 || seen.Length != 219 ||
            Serial >= ulong.MaxValue - 1) return null;
        var before = capture.Before;
        var caps = capture.Caps;
        bool changedBank = false, changedMain = false;
        for (int slot = 0; slot < before.Length; slot++)
        {
            if (!reports[slot].IsCanonical || (reports[slot].IsEmpty && reports[slot] != default) ||
                (reports[slot].ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0 ||
                (before[slot] != reports[slot] && !seen[slot])) return null;
            if (slot >= 59 && before[slot] != reports[slot]) changedBank = true;
            if (slot < 58 && before[slot] != reports[slot]) changedMain = true;
            int lane = QuantityLane(before[slot].ItemType);
            if (lane >= 0 && caps[slot * QuantityLaneCount + lane] >= 0 && before[slot].Stack > caps[slot * QuantityLaneCount + lane]) return null;
        }
        // Main-only comparison is an explicit caller policy. It never permits
        // an unreported bank or cursor change, or bypasses conserved identities.
        bool mainOnly = allowMainOnly && changedMain && !changedBank && before[58] == reports[58];
        if ((!changedBank && !mainOnly) || !ConservesBankCursorIdentities(before, reports)) return null;
        return new(this, connection, capture, reports.ToArray(), itemTime);
    }

    private static bool ConservesBankCursorIdentities(ReadOnlySpan<RuntimePlayerInventoryItem> before,
        ReadOnlySpan<RuntimePlayerInventoryItem> after)
    {
        // ItemSlot cursor primitives never perform DoCoins: denominations and
        // prefixes are identities here. Bulk normalization is a separate family.
        // Favorite can clear or be copied onto a larger compatible stack, so it
        // is not a conserved quantity; a favorite identity cannot arise from none.
        var quantities = new Dictionary<(int Type, int Prefix), long>();
        var favorites = new HashSet<(int Type, int Prefix)>();
        foreach (ref readonly var item in before)
        {
            if (item.IsEmpty) continue;
            var key = (item.ItemType.Value, item.Prefix.Value);
            quantities.TryGetValue(key, out long quantity);
            quantities[key] = checked(quantity + item.Stack);
            if ((item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) favorites.Add(key);
        }
        foreach (ref readonly var item in after)
        {
            if (item.IsEmpty) continue;
            var key = (item.ItemType.Value, item.Prefix.Value);
            if (!quantities.TryGetValue(key, out long quantity) ||
                ((item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0 && !favorites.Contains(key))) return false;
            quantities[key] = checked(quantity - item.Stack);
        }
        foreach (long quantity in quantities.Values) if (quantity != 0) return false;
        return true;
    }

    internal bool TryPrepareShopBankChains(ConnectionHandle connection, ShopJournalCapture capture,
        ReadOnlySpan<RuntimePlayerInventoryItem> reports, ReadOnlySpan<bool> seen,
        int animation, int itemTime, out ShopBankMovePreparation? prepared, out bool ambiguous)
    {
        prepared = null; ambiguous = false;
        var work = TryCreateShopBankChainWork(connection, capture, reports.ToArray(), seen.ToArray(), animation, itemTime);
        if (work is null) return false;
        using (work) while (work.MoveNext()) { }
        prepared = work.Preparation; ambiguous = work.Ambiguous;
        return prepared is not null && !ambiguous;
    }

    // Each advance performs one bounded candidate attempt or terminal comparison.
    // A candidate checks and then simulates the same source primitive. The journal
    // owner schedules advances under its existing global per-tick work budget.
    internal sealed class BankChainWork : IDisposable
    {
        private readonly RuntimePlayerInventoryStore owner;
        private readonly ConnectionHandle connection;
        private readonly ShopJournalCapture capture;
        private readonly RuntimePlayerInventoryItem[] target;
        private readonly int itemTime;
        private readonly IEnumerator<bool> walker;
        private bool disposed;
        private bool completed;
        private const int MaximumRememberedStates = 128;
        private readonly VisitedState?[] visited = new VisitedState?[MaximumRememberedStates];
        private int visitedCount;
        internal ShopBankMovePreparation? Preparation { get; private set; }
        internal bool Ambiguous { get; private set; }
        internal long Steps { get; private set; }

        internal BankChainWork(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
            ShopJournalCapture capture, RuntimePlayerInventoryItem[] target, int itemTime)
        {
            this.owner = owner; this.connection = connection; this.capture = capture;
            this.target = target; this.itemTime = itemTime;
            var image = capture.Before.ToArray();
            var stage = new byte[image.Length];
            for (int slot = 0; slot < image.Length; slot++)
                if (slot == 58 || image[slot] == target[slot]) stage[slot] = 2;
            walker = Walk(image, capture.Copper.ToArray(), capture.Quantity, capture.Caps, stage).GetEnumerator();
        }

        internal bool MoveNext()
        {
            if (Ambiguous || !capture.IsCurrent || owner.Serial >= ulong.MaxValue - 1)
            { Preparation = null; walker.Dispose(); return false; }
            if (disposed || completed) return false;
            bool next = walker.MoveNext();
            if (!next) completed = true;
            if (next) Steps++;
            return next;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            walker.Dispose();
            Array.Clear(visited);
            visitedCount = 0;
        }

        private IEnumerable<bool> Walk(RuntimePlayerInventoryItem[] image, long[] copper,
            short[] quantity, short[] caps, byte[] stage)
        {
            // A donor may be split before it reaches its final residual. Every
            // constructed Right/Left pair is a real primitive; source surplus and
            // target deficit decrease, so no unreported loop is reconstructed.
            foreach (bool step in VisitSplitSettlements())
            { yield return step; if (Ambiguous) yield break; }
            foreach (bool step in Visit(image, copper, quantity, caps, stage, false))
            { yield return step; if (Ambiguous) yield break; }
        }

        private IEnumerable<bool> VisitSplitSettlements()
        {
            if (itemTime != 0 || !capture.Before[58].IsEmpty || !target[58].IsEmpty) yield break;
            for (int order = 0; order < 6; order++)
            {
                var image = capture.Before.ToArray(); var copper = capture.Copper.ToArray();
                var quantity = capture.Quantity; var caps = capture.Caps;
                var sources = Enumerable.Range(0, image.Length).Where(i => i != 58);
                if (order >= 3) sources = sources.Reverse();
                bool valid = true;
                foreach (int source in sources)
                {
                    int retained = image[source].ItemType == target[source].ItemType &&
                        image[source].Prefix == target[source].Prefix ? target[source].Stack : 0;
                    int surplus = image[source].Stack - retained;
                    if (surplus <= 0 || image[source].IsEmpty) continue;
                    var destinations = Enumerable.Range(0, image.Length).Where(i => i != source && i != 58 &&
                        target[i].ItemType == image[source].ItemType && target[i].Prefix == image[source].Prefix &&
                        (image[i].IsEmpty || image[i].ItemType == target[i].ItemType && image[i].Prefix == target[i].Prefix) &&
                        target[i].Stack > image[i].Stack);
                    destinations = order % 3 == 0 ? destinations.OrderBy(i => image[i].IsEmpty ? 0 : 1).ThenBy(i => i)
                        : order % 3 == 1 ? destinations.OrderBy(i => i) : destinations.OrderByDescending(i => i);
                    foreach (int destination in destinations.ToArray())
                    {
                        int amount = Math.Min(surplus, target[destination].Stack - image[destination].Stack);
                        var residual = image[source].Stack == amount ? default
                            : image[source] with { Stack = checked((short)(image[source].Stack - amount)) };
                        int sourceContext = source < 59 ? VanillaInventoryCursorMove1458.OrdinaryContext
                            : source < 179 ? VanillaInventoryCursorMove1458.BankContext : VanillaInventoryCursorMove1458.VoidBankContext;
                        yield return true;
                        if (!TryBankPair(image, copper, quantity, caps, source, residual, null, true, itemTime, sourceContext))
                        { valid = false; break; }
                        var settled = target[destination] with { Stack = checked((short)(image[destination].Stack + amount)) };
                        int destinationContext = destination < 59 ? VanillaInventoryCursorMove1458.OrdinaryContext
                            : destination < 179 ? VanillaInventoryCursorMove1458.BankContext : VanillaInventoryCursorMove1458.VoidBankContext;
                        yield return true;
                        if (!TryBankPair(image, copper, quantity, caps, destination, settled, default(RuntimePlayerInventoryItem),
                            false, itemTime, destinationContext)) { valid = false; break; }
                        surplus -= amount;
                        if (surplus == 0) break;
                    }
                    if (!valid) break;
                }
                if (!valid || !image.AsSpan().SequenceEqual(target)) continue;
                // Reuse the exact final-image/custody/cap comparison of the DFS.
                foreach (bool step in Visit(image, copper, quantity, caps, new byte[image.Length], false))
                { yield return step; if (Ambiguous) yield break; }
            }
        }

        // Exact state memoization preserves every possible continuation. Fingerprints
        // only filter comparisons; a hit requires the full physical/private/stage image.
        // The fixed cache bounds retained memory, not the search: once full, new states
        // are explored normally without being inserted. Stages/relay progress prevent
        // an identical state from being a live ancestor of its own continuation.
        private bool WasVisited(RuntimePlayerInventoryItem[] image, long[] copper,
            short[] quantity, short[] caps, byte[] stage, bool relayed)
        {
            var hash = new HashCode();
            foreach (var item in image) hash.Add(item);
            foreach (long value in copper) hash.Add(value);
            foreach (short value in quantity) hash.Add(value);
            foreach (short value in caps) hash.Add(value);
            foreach (byte value in stage) hash.Add(value);
            hash.Add(relayed);
            int fingerprint = hash.ToHashCode();
            for (int i = 0; i < visitedCount; i++)
            {
                if (visited[i] is not { } state) continue;
                if (state.Fingerprint == fingerprint && state.Relayed == relayed &&
                    state.Image.AsSpan().SequenceEqual(image) &&
                    state.Copper.AsSpan().SequenceEqual(copper) &&
                    state.Quantity.AsSpan().SequenceEqual(quantity) &&
                    state.Caps.AsSpan().SequenceEqual(caps) &&
                    state.Stage.AsSpan().SequenceEqual(stage)) return true;
            }
            if (visitedCount < visited.Length)
                visited[visitedCount++] = new(fingerprint, image.ToArray(), copper.ToArray(),
                    quantity.ToArray(), caps.ToArray(), stage.ToArray(), relayed);
            return false;
        }

        private sealed record VisitedState(int Fingerprint, RuntimePlayerInventoryItem[] Image,
            long[] Copper, short[] Quantity, short[] Caps, byte[] Stage, bool Relayed);

        // Progress stages only increase: untouched -> opened -> final. At most
        // 218 noncursor slots can change, bounding active depth to 437 frames
        // including the one equivalent favorite-clearing relay. This reconciles
        // reduced source paths; unrestricted unreported UI cycles are not inferred.
        private IEnumerable<bool> Visit(RuntimePlayerInventoryItem[] image, long[] copper,
            short[] quantity, short[] caps, byte[] stage, bool relayed)
        {
            if (WasVisited(image, copper, quantity, caps, stage, relayed)) yield break;
            if (image.AsSpan().SequenceEqual(target))
            {
                var finalCaps = capture.Caps;
                ReconcileFinalBankCaps(capture.Before, target, finalCaps);
                if (AreValidRetainedMainShopImage(target.AsSpan(0, 59), quantity.AsSpan(0, 59).ToArray(),
                    copper.AsSpan(0, 59).ToArray(), finalCaps.AsSpan(0, 59 * QuantityLaneCount)) &&
                    AreValidShopBanks(target.AsSpan(59), copper.AsSpan(59)) &&
                    AreValidShopBankOwnership(target.AsSpan(59), quantity.AsSpan(59), finalCaps.AsSpan(59 * QuantityLaneCount)))
                {
                    var candidate = new ShopBankMovePreparation(owner, connection, capture,
                        target.ToArray(), copper.ToArray(), quantity.ToArray(), finalCaps);
                    if (Preparation is not null && !Preparation.HasSameAcceptedImage(candidate))
                    { Ambiguous = true; Preparation = null; }
                    else Preparation = candidate;
                }
                yield return true;
                yield break;
            }
            for (int slot = 0; slot < image.Length; slot++)
            {
                if (slot == 58 || stage[slot] == 2) continue;
                int context = slot < 59 ? VanillaInventoryCursorMove1458.OrdinaryContext
                    : slot < 179 ? VanillaInventoryCursorMove1458.BankContext : VanillaInventoryCursorMove1458.VoidBankContext;
                for (int family = 0; family < 2; family++)
                {
                    var source = StorageTuple(image[slot]); var cursor = StorageTuple(image[58]);
                    WorldItemPickupSlot1458 nextSlot = default, nextCursor = default;
                    bool valid = family == 0
                        ? itemTime == 0 && VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in cursor,
                            out nextSlot, out nextCursor, context)
                        : VanillaInventoryCursorMove1458.TryPlanToCursor(in source, in cursor,
                            source.Stack - target[slot].Stack, out nextSlot, out nextCursor, context);
                    yield return true;
                    if (!valid) continue;
                    var next = InventoryTuple(nextSlot);
                    byte nextStage = next == target[slot] ? (byte)2
                        : stage[slot] == 0 && !image[slot].IsEmpty && next.IsEmpty ? (byte)1 : (byte)0;
                    if (nextStage == 0) continue;
                    var child = image.ToArray(); var childCopper = copper.ToArray();
                    var childQuantity = quantity.ToArray(); var childCaps = caps.ToArray();
                    if (!TryBankPair(child, childCopper, childQuantity, childCaps, slot,
                        next, InventoryTuple(nextCursor), family != 0, itemTime, context)) continue;
                    var childStage = stage.ToArray(); childStage[slot] = nextStage;
                    foreach (bool step in Visit(child, childCopper, childQuantity, childCaps, childStage, relayed))
                    { yield return step; if (Ambiguous) yield break; }
                }
            }
            // A physically unchanged empty-bank relay can change cursor favorite.
            // Execute the actual two context4 primitives; it is not a global flag reset.
            if (!relayed && itemTime == 0 && !image[58].IsEmpty &&
                (image[58].ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)
            {
                for (int slot = 59; slot < 179; slot++)
                {
                    if (!image[slot].IsEmpty || !target[slot].IsEmpty) continue;
                    var empty = StorageTuple(image[slot]); var cursor = StorageTuple(image[58]);
                    bool valid = VanillaInventoryCursorMove1458.TryPlanLeftClick(in empty, in cursor,
                        out var deposited, out var clearedCursor, VanillaInventoryCursorMove1458.BankContext);
                    yield return true;
                    if (!valid) continue;
                    valid = VanillaInventoryCursorMove1458.TryPlanLeftClick(in deposited, in clearedCursor,
                        out var restored, out var returned, VanillaInventoryCursorMove1458.BankContext);
                    yield return true;
                    if (!valid || InventoryTuple(restored) != image[slot] || InventoryTuple(returned) == image[58]) continue;
                    var child = image.ToArray(); child[58] = InventoryTuple(returned);
                    foreach (bool step in Visit(child, copper, quantity, caps, stage, true))
                    { yield return step; if (Ambiguous) yield break; }
                    // All canonical empty context4 relays have identical custody and flags.
                    break;
                }
            }
        }
    }
}
