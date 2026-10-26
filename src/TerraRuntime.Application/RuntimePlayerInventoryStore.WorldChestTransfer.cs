using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    private const int MaximumWorldTransferAlternatives = 64;

    internal sealed class WorldTransferResolution
    {
        internal ShopBankMovePreparation? Inventory;
        internal WorldChestItem[]? Chest;
        internal short[]? ChestQuantity;
        internal long[]? ChestCopper;
        internal bool Ambiguous;
    }

    // The journal, rather than raw32, supplies the retained complete inventory.
    // The strict world owner supplies loaded/previously proved full custody.
    internal IEnumerable<int> PrepareWorldChestTransfer(ConnectionHandle connection,
        ShopJournalCapture capture, RuntimeChestStore.OwnedTransferCapture world,
        IReadOnlyList<TerrariaChestItemState> reports, RuntimePlayerInventoryItem[] wanted,
        int animation, int itemTime, WorldTransferResolution result)
    {
        if (!capture.IsCurrent || !world.IsCurrent || capture.Escrow is not null ||
            animation != 0 || itemTime != 0 || wanted.Length != 219 || reports.Count is < 1 or > 255)
            yield break;
        int count = 219 + world.Before.Length;
        var image = new RuntimePlayerInventoryItem[count];
        var copper = new long[count];
        var quantity = new short[count];
        var caps = new short[count * QuantityLaneCount];
        Array.Fill(caps, (short)-1);
        capture.Before.CopyTo(image, 0);
        capture.Copper.CopyTo(copper, 0);
        capture.Quantity.CopyTo(quantity, 0);
        capture.Caps.CopyTo(caps, 0);
        for (int slot = 0; slot < world.Before.Length; slot++)
        {
            yield return 1;
            if (!TryWorldInventoryTuple(world.Before[slot], out var item)) yield break;
            int index = 219 + slot;
            image[index] = item;
            copper[index] = world.Copper[slot];
            quantity[index] = world.Quantity[slot];
        }
        // Source compound UI calls may change one main/bank position without a
        // cursor58 report. The complete219 target still validates the result.
        // Larger composed producers remain a separate bounded extension.
        int actorSlot = -1;
        for (int index = 0; index < 219; index++)
        {
            yield return 1;
            if (index == 58 || capture.Before[index] == wanted[index]) continue;
            if (actorSlot >= 0 || (index >= 50 && index < 59)) { actorSlot = -2; break; }
            actorSlot = index;
        }
        var states = new List<WorldAllocation> { new(image, copper, quantity, caps) };
        foreach (TerrariaChestItemState report in reports)
        {
            yield return 1;
            if (report.ChestId != world.ChestId || report.ItemSlot >= world.Before.Length ||
                !TryWorldInventoryTuple(new(report.Stack, report.ItemNetId, report.Prefix), out var after)) yield break;
            var readyStates = new List<WorldAllocation>();
            foreach (WorldAllocation state in states)
            {
                yield return 1;
                if (!readyStates.Any(existing => existing.Same(state))) readyStates.Add(state);
                if (readyStates.Count > MaximumWorldTransferAlternatives) { result.Ambiguous = true; yield break; }
                if (actorSlot >= 0)
                    foreach (WorldAllocation next in PrepareWorldActorLeg(state, actorSlot, wanted[actorSlot], itemTime))
                    {
                        yield return 1;
                        if (!readyStates.Any(existing => existing.Same(next))) readyStates.Add(next);
                        if (readyStates.Count > MaximumWorldTransferAlternatives) { result.Ambiguous = true; yield break; }
                    }
            }
            var nextStates = new List<WorldAllocation>();
            foreach (WorldAllocation state in readyStates)
            {
                for (int family = 0; family < 2; family++)
                {
                    yield return 1;
                    var candidate = state.Copy();
                    int index = 219 + report.ItemSlot;
                    bool accepted = TryBankPair(candidate.Items, candidate.Copper, candidate.Quantity,
                        candidate.Caps, index, after, null, family != 0, itemTime,
                        VanillaInventoryCursorMove1458.WorldChestContext);
                    // Original full identical Left exchanges object references and
                    // sends32 even though the generic observable planner returnsfalse.
                    // This is detached explanation only, never a live ACK budget swap.
                    if (!accepted && family == 0 && candidate.Items[index] == after &&
                        candidate.Items[index] == candidate.Items[58] && !after.IsEmpty &&
                        after.ItemFlags == 0 && VanillaWorldItemAllocationCatalog1458.TryGet(after.ItemType.Value,
                            out var facts) && after.Stack == facts.MaximumStack)
                    {
                        (candidate.Quantity[index], candidate.Quantity[58]) = (candidate.Quantity[58], candidate.Quantity[index]);
                        (candidate.Copper[index], candidate.Copper[58]) = (candidate.Copper[58], candidate.Copper[index]);
                        accepted = true;
                    }
                    if (!accepted || nextStates.Any(existing => existing.Same(candidate))) continue;
                    nextStates.Add(candidate);
                    if (nextStates.Count > MaximumWorldTransferAlternatives) { result.Ambiguous = true; yield break; }
                }
            }
            if (nextStates.Count == 0) yield break;
            states = nextStates;
        }
        var finalStates = new List<WorldAllocation>();
        foreach (WorldAllocation state in states)
        {
            yield return 1;
            if (!finalStates.Any(existing => existing.Same(state))) finalStates.Add(state);
            if (finalStates.Count > MaximumWorldTransferAlternatives) { result.Ambiguous = true; yield break; }
            if (actorSlot >= 0)
                foreach (WorldAllocation next in PrepareWorldActorLeg(state, actorSlot, wanted[actorSlot], itemTime))
                {
                    yield return 1;
                    if (!finalStates.Any(existing => existing.Same(next))) finalStates.Add(next);
                    if (finalStates.Count > MaximumWorldTransferAlternatives) { result.Ambiguous = true; yield break; }
                }
        }
        foreach (WorldAllocation state in finalStates)
        {
            yield return 1;
            if (!state.Items.AsSpan(0, 219).SequenceEqual(wanted)) continue;
            bool fullWorld = true;
            var afterWorld = new WorldChestItem[world.Before.Length];
            for (int slot = 0; slot < world.Before.Length; slot++)
            {
                yield return 1;
                int index = 219 + slot;
                RuntimePlayerInventoryItem item = state.Items[index];
                long value = CoinValue(item);
                if (state.Copper[index] != value || state.Quantity[index] != (value == 0 ? item.Stack : 0))
                    fullWorld = false;
                afterWorld[slot] = new(item.Stack, item.ItemType.Value, checked((byte)item.Prefix.Value));
            }
            // Partial world slots require durable ownership, which this strict
            // transitional mode does not yet persist. Preserve both preimages.
            if (!fullWorld && !world.SupportsDurableCustody) continue;
            bool unique = true;
            var groups = new Dictionary<(ItemTypeId Type, PrefixId Prefix), (long Physical, long Funded, int Destinations)>();
            for (int index = 0; index < count; index++)
            {
                yield return 1;
                var item = state.Items[index];
                if (item.IsEmpty) continue;
                var key = (item.ItemType, item.Prefix);
                groups.TryGetValue(key, out var group);
                long physical = CoinValue(item);
                long funded = physical > 0 ? state.Copper[index] : state.Quantity[index];
                if (physical == 0) physical = item.Stack;
                groups[key] = (checked(group.Physical + physical), checked(group.Funded + funded), group.Destinations + 1);
            }
            foreach (var group in groups.Values)
                if (group.Funded != 0 && group.Funded != group.Physical && group.Destinations > 1) unique = false;
            // This sufficient allocation certificate covers every conserving
            // private allocation. Absence is a conservative refusal, not uniqueness.
            if (!unique) { result.Ambiguous = true; continue; }
            var actorItems = state.Items.AsSpan(0, 219).ToArray();
            var actorCopper = state.Copper.AsSpan(0, 219).ToArray();
            var actorQuantity = state.Quantity.AsSpan(0, 219).ToArray();
            var actorCaps = capture.Caps;
            ReconcileFinalBankCaps(capture.Before, actorItems, actorCaps);
            if (!AreValidRetainedMainShopImage(actorItems.AsSpan(0, 59), actorQuantity.AsSpan(0, 59).ToArray(),
                    actorCopper.AsSpan(0, 59).ToArray(), actorCaps.AsSpan(0, 59 * QuantityLaneCount)) ||
                !AreValidShopBanks(actorItems.AsSpan(59), actorCopper.AsSpan(59)) ||
                !AreValidShopBankOwnership(actorItems.AsSpan(59), actorQuantity.AsSpan(59), actorCaps.AsSpan(59 * QuantityLaneCount))) continue;
            var prepared = new ShopBankMovePreparation(this, connection, capture, actorItems, actorCopper, actorQuantity, actorCaps);
            if (result.Inventory is { } prior && (!prior.HasSameAcceptedImage(prepared) || !result.Chest!.AsSpan().SequenceEqual(afterWorld) ||
                !result.ChestQuantity!.AsSpan().SequenceEqual(state.Quantity.AsSpan(219)) ||
                !result.ChestCopper!.AsSpan().SequenceEqual(state.Copper.AsSpan(219))))
            { result.Ambiguous = true; result.Inventory = null; result.Chest = null; yield break; }
            result.Inventory = prepared;
            result.Chest = afterWorld;
            result.ChestQuantity = state.Quantity.AsSpan(219).ToArray();
            result.ChestCopper = state.Copper.AsSpan(219).ToArray();
        }
        if (result.Ambiguous) { result.Inventory = null; result.Chest = null; }
    }

    private static IEnumerable<WorldAllocation> PrepareWorldActorLeg(WorldAllocation state, int slot,
        RuntimePlayerInventoryItem target, int itemTime)
    {
        int context = slot < 50 ? VanillaInventoryCursorMove1458.OrdinaryContext :
            slot >= 179 ? VanillaInventoryCursorMove1458.VoidBankContext : VanillaInventoryCursorMove1458.BankContext;
        var source = StorageTuple(state.Items[slot]);
        var cursor = StorageTuple(state.Items[58]);
        if (itemTime == 0 && VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in cursor,
            out var nextSlot, out var nextCursor, context))
        {
            var next = state.Copy();
            if (TryBankPair(next.Items, next.Copper, next.Quantity, next.Caps, slot,
                InventoryTuple(nextSlot), InventoryTuple(nextCursor), false, itemTime, context)) yield return next;
        }
        var right = state.Copy();
        if (TryBankPair(right.Items, right.Copper, right.Quantity, right.Caps, slot,
            target, null, true, itemTime, context)) yield return right;
    }

    private static bool TryWorldInventoryTuple(in WorldChestItem source, out RuntimePlayerInventoryItem item)
    {
        item = default;
        if (source.Stack == 0) return source.ItemType == 0 && source.Prefix == 0;
        if (source.Stack is < 0 or > short.MaxValue || !source.TryGetItemType(out var type) || type.IsNone) return false;
        item = new(type, (short)source.Stack, source.PrefixId, 0);
        return item.IsCanonical;
    }

    private sealed record WorldAllocation(RuntimePlayerInventoryItem[] Items, long[] Copper, short[] Quantity, short[] Caps)
    {
        internal WorldAllocation Copy() => new(Items.ToArray(), Copper.ToArray(), Quantity.ToArray(), Caps.ToArray());
        internal bool Same(WorldAllocation other) => Items.AsSpan().SequenceEqual(other.Items) &&
            Copper.AsSpan().SequenceEqual(other.Copper) && Quantity.AsSpan().SequenceEqual(other.Quantity) &&
            Caps.AsSpan().SequenceEqual(other.Caps);
    }
}
