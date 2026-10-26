using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // A positive source-primitive witness rejects an otherwise accepted image.
    // Exhausting these witnesses never proves uniqueness or skips other families.
    private IEnumerable<int> PrepareClosedCursorWitnesses(ShopJournalCapture capture,
        RuntimePlayerInventoryItem[] accepted, long[] acceptedCopper, short[] acceptedQuantity,
        short[] acceptedCaps, int animation, int itemTime, BankBulkResolution result)
    {
        if (animation != 0 || itemTime != 0 || !accepted[58].IsEmpty) yield break;
        // Closed cursor paths preserve each exact identity's budget. Zero/full
        // groups and single destinations cannot produce a positive witness. This
        // eligibility check never elides comparison with other source producers.
        var groups = new Dictionary<(int Type, int Prefix), (long Physical, long Funded, int Slots)>();
        for (int slot = 0; slot < accepted.Length; slot++)
        {
            yield return 0;
            var item = accepted[slot];
            if (item.IsEmpty) continue;
            long coin = CoinValue(item);
            long physical = coin > 0 ? coin : item.Stack;
            long funded = coin > 0 ? acceptedCopper[slot] : acceptedQuantity[slot];
            if (funded < 0 || funded > physical) yield break;
            var key = (item.ItemType.Value, item.Prefix.Value);
            groups.TryGetValue(key, out var group);
            groups[key] = (checked(group.Physical + physical), checked(group.Funded + funded), group.Slots + 1);
        }
        var image = new RuntimePlayerInventoryItem[accepted.Length];
        var copper = new long[acceptedCopper.Length];
        var quantity = new short[acceptedQuantity.Length];
        var caps = new short[acceptedCaps.Length];
        var mainQuantity = new short[59];
        var mainCopper = new long[59];
        for (int a = 0; a < accepted.Length; a++)
        {
            if (a == 58 || accepted[a].IsEmpty) continue;
            var group = groups[(accepted[a].ItemType.Value, accepted[a].Prefix.Value)];
            if (group.Funded == 0 || group.Funded == group.Physical || group.Slots == 1) continue;
            for (int b = 0; b < accepted.Length; b++)
            {
                // Every ordered pair is charged to the same cooperative work budget.
                yield return 0;
                if (a == b || b == 58 || accepted[b].IsEmpty ||
                    accepted[a].ItemType != accepted[b].ItemType ||
                    accepted[a].Prefix != accepted[b].Prefix) continue;
                // Enumerate both the source three-Left whole-object exchange
                // and the existing merge/right/return witness. Currency's
                // Main endpoints of this new whole-object family are 0..49 only;
                // coin/ammo UI roles 1/2 cannot be modeled as ordinary context 0.
                // Currency's identical-middle certificate here is bank-only: ordinary
                // DoCoins must not be assumed inert at the nominal carry limit.
                for (int path = 0; path < 2; path++)
                {
                    bool fullExchange = path == 0;
                    if (fullExchange &&
                        ((a is >= 50 and < 59) || (b is >= 50 and < 59) ||
                         !VanillaWorldItemAllocationCatalog1458.TryGet(accepted[a].ItemType.Value, out var fullFacts) ||
                         accepted[a].Stack != fullFacts.MaximumStack || accepted[b].Stack != fullFacts.MaximumStack ||
                         (CoinValue(accepted[a]) > 0 && (a < 59 || b < 59)))) continue;
                    accepted.CopyTo(image, 0); acceptedCopper.CopyTo(copper, 0);
                    acceptedQuantity.CopyTo(quantity, 0); acceptedCaps.CopyTo(caps, 0);
                    int amount = accepted[a].Stack;
                    // Left whole A, Left merge B, repeated Right amount, Left return A.
                    // Each actual planned primitive is checked again by TryBankPair.
                    bool valid = true;
                    for (int leg = 0; leg < (fullExchange ? 3 : 4); leg++)
                    {
                        int repetitions = !fullExchange && leg == 2 ? amount : 1;
                        for (int repeat = 0; repeat < repetitions; repeat++)
                        {
                            yield return 0;
                            int slot = leg == 0 || leg == (fullExchange ? 2 : 3) ? a : b;
                            int context = slot is >= 50 and < 54 ? VanillaInventoryCursorMove1458.CoinContext :
                                slot < 59 ? VanillaInventoryCursorMove1458.OrdinaryContext :
                                slot < 179 ? VanillaInventoryCursorMove1458.BankContext :
                                VanillaInventoryCursorMove1458.VoidBankContext;
                            var source = StorageTuple(image[slot]); var cursor = StorageTuple(image[58]);
                            // Original context2 gates Left by the incoming item's
                            // FitsAmmoSlot; Right only withdraws and has no such gate.
                            // Once eligible, its tuple transform is ordinary context0.
                            bool right = !fullExchange && leg == 2;
                            if (!right && slot is >= 54 and < 58 && cursor.Type != 0 &&
                                (!VanillaWorldItemPickupCatalog1458.TryGet(cursor.Type, out var ammoFacts) ||
                                 !ammoFacts.FitsAmmoSlot))
                            { valid = false; break; }
                            WorldItemPickupSlot1458 nextSlot, nextCursor;
                            bool planned = !fullExchange && leg == 2
                                ? VanillaInventoryCursorMove1458.TryPlanToCursor(in source, in cursor, 1,
                                    out nextSlot, out nextCursor, context)
                                : VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in cursor,
                                    out nextSlot, out nextCursor, context);
                            // Original full-capacity matching Left swaps the objects,
                            // even when both public tuples remain identical. The tuple
                            // primitive returns false for that public no-op; its out
                            // images still certify this narrowly source-proven branch.
                            if (fullExchange && leg == 1 && !planned &&
                                (source == cursor ||
                                 (context is VanillaInventoryCursorMove1458.OrdinaryContext or VanillaInventoryCursorMove1458.VoidBankContext &&
                                  (source with { Favorite = cursor.Favorite }) == cursor)) &&
                                nextSlot == source && nextCursor == cursor)
                            {
                                (copper[slot], copper[58]) = (copper[58], copper[slot]);
                                (quantity[slot], quantity[58]) = (quantity[58], quantity[slot]);
                                continue;
                            }
                            if (!planned || !TryBankPair(image, copper, quantity, caps, slot,
                                InventoryTuple(nextSlot), InventoryTuple(nextCursor), !fullExchange && leg == 2, itemTime, context))
                            { valid = false; break; }
                        }
                        if (!valid) break;
                    }
                    if (!valid || !image.AsSpan().SequenceEqual(accepted)) continue;
                    // The shared journal represents caps by original -> final lanes.
                    var finalCaps = capture.Caps;
                    ReconcileFinalBankCaps(capture.Before, image, finalCaps);
                    quantity.AsSpan(0, 59).CopyTo(mainQuantity);
                    copper.AsSpan(0, 59).CopyTo(mainCopper);
                    if (!finalCaps.AsSpan().SequenceEqual(acceptedCaps) ||
                        !AreValidRetainedMainShopImage(image.AsSpan(0, 59), mainQuantity, mainCopper,
                            finalCaps.AsSpan(0, 59 * QuantityLaneCount)) ||
                        !AreValidShopBanks(image.AsSpan(59), copper.AsSpan(59)) ||
                        !AreValidShopBankOwnership(image.AsSpan(59), quantity.AsSpan(59),
                            finalCaps.AsSpan(59 * QuantityLaneCount))) continue;
                    if (!copper.AsSpan().SequenceEqual(acceptedCopper) ||
                        !quantity.AsSpan().SequenceEqual(acceptedQuantity))
                    { result.Ambiguous = true; yield break; }
                }
            }
        }
    }
}
