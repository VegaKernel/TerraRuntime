using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // The same committed external material slice retains its exact private
    // budget; physical amount alone is not a payment certificate.
    internal readonly record struct CraftExternalMaterial(WorldItemPickupSlot1458 Item, int Quantity, long Copper);

    // Detached recipe custody working state. It reuses the same physical and
    // private arrays and the final bank-journal token; no raw report credits it.
    internal CraftWorkingImage? TryCreateCraftWorking(ConnectionHandle connection, ShopJournalCapture capture)
    {
        if (!capture.IsCurrent || !CanAccept(connection)) return null;
        return new(this, connection, capture, capture.Before.ToArray(), capture.Copper.ToArray(), capture.Quantity, capture.Caps);
    }

    internal sealed class CraftWorkingImage(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ShopJournalCapture capture, RuntimePlayerInventoryItem[] image, long[] copper, short[] quantity, short[] caps)
    {
        internal RuntimePlayerInventoryItem[] Image => image;
        private long[] Copper => copper;
        private short[] Quantity => quantity;
        private short[] Caps => caps;
        internal CraftWorkingImage Clone() => new(owner, connection, capture, image.ToArray(), copper.ToArray(), quantity.ToArray(), caps.ToArray());
        internal bool HasSamePrivateImage(CraftWorkingImage other) => image.AsSpan().SequenceEqual(other.Image) &&
            copper.AsSpan().SequenceEqual(other.Copper) && quantity.AsSpan().SequenceEqual(other.Quantity) && caps.AsSpan().SequenceEqual(other.Caps);
        internal bool HasUniqueCraftAllocation() =>
            HasUniqueConservativeFunding(capture.Before, capture.Quantity, capture.Copper,
                capture.Before, capture.Quantity, capture.Copper) &&
            HasUniqueConservativeFunding(image, quantity, copper, image, quantity, copper);

        internal bool TryApply(WorldItemCraftPlan1458 plan)
            => plan.ExternalDebits.Length == 0 && TryApply(plan, [], []);

        internal bool TryApply(WorldItemCraftPlan1458 plan, ReadOnlySpan<WorldItemCraftExternalLeg1458> paidExternalDebits,
            ReadOnlySpan<CraftExternalMaterial> externalFunding)
        {
            if (!paidExternalDebits.SequenceEqual(plan.ExternalDebits) || externalFunding.Length != paidExternalDebits.Length) return false;
            if (!capture.IsCurrent || plan.After.Length != 219 ||
                !VanillaRecipeCatalog1458.TryGet(plan.RecipeIndex, out var recipe) ||
                !TryCraftItem(plan.Result, out var result) || result.IsEmpty ||
                result.Stack != recipe.ResultStack || result.ItemType.Value != (recipe.ResultType.Value == 5437 ? 5358 : recipe.ResultType.Value)) return false;
            bool currency = plan.RecipeIndex is >= 3597 and <= 3602;
            // Growth-only observational alchemy needs an actual owned lottery
            // certificate. The client RNG is not disclosed by packet5/138.
            if (!currency && plan.Debits.Length == 0 && paidExternalDebits.IsEmpty) return false;
            var next = image.ToArray(); var nextCopper = copper.ToArray();
            var nextQuantity = quantity.ToArray(); var nextCaps = caps.ToArray();
            var nominal = image.ToArray(); var required = new long[219];
            foreach (var leg in plan.NominalReservations)
            {
                if (!TryReplayCraftDebit(nominal, in leg)) return false;
                required[leg.SourceSlot] = checked(required[leg.SourceSlot] + leg.Amount);
            }
            for (int slot = 0; slot < 219; slot++)
            {
                if (required[slot] == 0) continue;
                long unit = CoinValue(image[slot]) > 0 ? CoinValue(image[slot]) / image[slot].Stack : 0;
                if (currency)
                {
                    if (unit == 0) return false;
                }
                else if (unit > 0 ? nextCopper[slot] < required[slot] * unit : nextQuantity[slot] < required[slot]) return false;
            }
            long consumedCopper = 0;
            bool externalQuantityPaid = true;
            for (int index = 0; index < paidExternalDebits.Length; index++)
            {
                var external = paidExternalDebits[index];
                var funding = externalFunding[index];
                if (external.Amount <= 0 || external.Amount > external.Item.Stack ||
                    funding.Item != (external.Item with { Stack = external.Amount }) ||
                    funding.Quantity < 0 || funding.Copper < 0 ||
                    !TryCraftItem(external.Item, out var material) || material.IsEmpty) return false;
                long value = CoinValue(material);
                if (value > 0)
                {
                    if (funding.Quantity != 0 || funding.Copper > value / material.Stack * external.Amount) return false;
                    consumedCopper = checked(consumedCopper + funding.Copper);
                }
                else
                {
                    if (funding.Copper != 0 || funding.Quantity > external.Amount) return false;
                    externalQuantityPaid &= funding.Quantity == external.Amount;
                }
            }
            foreach (var leg in plan.Debits)
            {
                if (!TryReplayCraftDebit(next, in leg)) return false;
                long unit = CoinValue(image[leg.SourceSlot]) > 0 ? CoinValue(image[leg.SourceSlot]) / image[leg.SourceSlot].Stack : 0;
                if (unit > 0)
                {
                    long taken = Math.Min(nextCopper[leg.SourceSlot], checked(unit * leg.Amount));
                    nextCopper[leg.SourceSlot] -= taken;
                    consumedCopper = checked(consumedCopper + taken);
                }
                else
                {
                    if (nextQuantity[leg.SourceSlot] < leg.Amount) return false;
                    nextQuantity[leg.SourceSlot] = checked((short)(nextQuantity[leg.SourceSlot] - leg.Amount));
                }
            }
            var oldCursor = next[58];
            if (!oldCursor.IsEmpty && (oldCursor.ItemType != result.ItemType || oldCursor.Prefix != result.Prefix)) return false;
            int combined = checked(result.Stack + Math.Max(0, (int)oldCursor.Stack));
            if (combined > short.MaxValue) return false;
            next[58] = result with { Stack = (short)combined };
            if (CoinValue(result) > 0)
            {
                if (!currency || consumedCopper > CoinValue(result)) return false;
                nextCopper[58] = checked(nextCopper[58] + consumedCopper);
                nextQuantity[58] = 0;
            }
            else
            {
                if (currency || consumedCopper != 0) return false;
                nextQuantity[58] = checked((short)(nextQuantity[58] + (externalQuantityPaid ? result.Stack : 0)));
                nextCopper[58] = 0;
            }
            foreach (var grant in plan.StorageGrants)
            {
                if (grant.Slot is < 0 or >= 58 || !TryCraftItem(grant.Before, out var before) ||
                    !TryCraftItem(grant.After, out var after) || next[grant.Slot] != before || after.IsEmpty ||
                    after.ItemType != result.ItemType || after.Prefix != result.Prefix ||
                    (!before.IsEmpty && (before.ItemType != after.ItemType || before.Prefix != after.Prefix))) return false;
                int taken = after.Stack - Math.Max(0, (int)before.Stack);
                if (taken <= 0 || taken > next[58].Stack) return false;
                if (CoinValue(result) > 0)
                {
                    long moved = Math.Min(nextCopper[58], CoinValue(result) / result.Stack * taken);
                    nextCopper[58] -= moved; nextCopper[grant.Slot] = checked(nextCopper[grant.Slot] + moved);
                }
                else
                {
                    int moved = Math.Min(nextQuantity[58], taken);
                    nextQuantity[58] -= (short)moved;
                    nextQuantity[grant.Slot] = checked((short)(nextQuantity[grant.Slot] + moved));
                }
                next[grant.Slot] = after;
                next[58] = taken == next[58].Stack ? default : next[58] with { Stack = (short)(next[58].Stack - taken) };
            }
            if (!TryCraftItem(plan.StorageResidual, out var residual) || next[58] != residual) return false;
            for (int slot = 0; slot < 219; slot++)
                if (!TryCraftItem(plan.After[slot], out var final) || next[slot] != final) return false;
            ReconcileFinalBankCaps(image, next, nextCaps);
            if (!AreValidRetainedMainShopImage(next.AsSpan(0,59), nextQuantity.AsSpan(0,59).ToArray(),
                    nextCopper.AsSpan(0,59).ToArray(), nextCaps.AsSpan(0,(59 * QuantityLaneCount))) ||
                !AreValidShopBanks(next.AsSpan(59), nextCopper.AsSpan(59)) ||
                !AreValidShopBankOwnership(next.AsSpan(59), nextQuantity.AsSpan(59), nextCaps.AsSpan((59 * QuantityLaneCount)))) return false;
            image = next; copper = nextCopper; quantity = nextQuantity; caps = nextCaps;
            return true;
        }
        internal ShopBankMovePreparation? TryPrepareAccepted(ReadOnlySpan<RuntimePlayerInventoryItem> observed)
            => capture.IsCurrent && observed.SequenceEqual(image) ? new(owner, connection, capture, image, copper, quantity, caps) : null;
    }

    private static bool TryReplayCraftDebit(RuntimePlayerInventoryItem[] image, in WorldItemCraftLeg1458 leg)
    {
        if (leg.SourceSlot is < 0 or >= 219 || leg.SourceSlot == 58 || leg.Amount <= 0 ||
            !TryCraftItem(leg.Before, out var before) || !TryCraftItem(leg.After, out var after) ||
            image[leg.SourceSlot] != before || before.IsEmpty || leg.Amount > before.Stack ||
            after != (leg.Amount == before.Stack ? default : before with { Stack = (short)(before.Stack - leg.Amount) })) return false;
        image[leg.SourceSlot] = after;
        return true;
    }
    private static bool TryCraftItem(in WorldItemPickupSlot1458 item, out RuntimePlayerInventoryItem result)
    {
        result = default;
        if (item.Type == 0) return item == default;
        if (item.Type is < 0 or > short.MaxValue || item.Stack is <= 0 or > short.MaxValue || item.Prefix is < 0 or > byte.MaxValue) return false;
        result = new(new(item.Type), (short)item.Stack, new(item.Prefix), item.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
        return result.IsCanonical;
    }
}