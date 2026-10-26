using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    internal bool TryPrepareFishingRewards(ConnectionHandle connection, short debitSlot, int debitType,
        out FishingRewardPreparation? preparation, ShopJournalCapture? sourceCapture = null)
    {
        preparation = null;
        var before = new RuntimePlayerInventoryItem[InventorySlotCount];
        if (Serial >= ulong.MaxValue - 1 || !TryCopyInventory(connection, before) ||
            sourceCapture is not null && (!sourceCapture.IsFor(this, connection) || !sourceCapture.IsCurrent)) return false;
        var liveBefore = before.ToArray();
        if (sourceCapture is not null)
        {
            if (sourceCapture.Escrow is not null) return false;
            sourceCapture.Before.AsSpan(0, InventorySlotCount).CopyTo(before);
        }
        var quantities = sourceCapture is null ? CaptureFundedQuantities(connection) :
            sourceCapture.Quantity.AsSpan(0, InventorySlotCount).ToArray();
        for (short slot = 0; slot < before.Length; slot++)
            if (!before[slot].IsCanonical || !CanAcceptOwnedQuantityReport(connection,
                    before[slot].ToCommitRequest(connection.Player.Slot, slot))) return false;
        if (debitSlot != -1 && (debitSlot is < 0 or >= 58 || before[debitSlot].ItemType.Value != debitType ||
            before[debitSlot].IsEmpty || quantities[debitSlot] <= 0)) return false;
        preparation = new(this, connection, Serial, before, quantities, debitSlot, debitType,
            sourceCapture is null ? CaptureFundedCoinValues(connection) :
                sourceCapture.Copper.AsSpan(0, InventorySlotCount).ToArray(), sourceCapture, liveBefore);
        return true;
    }

    // Detached source GetItem journal over the existing physical inventory and private
    // quantities. It owns no rewards or durable ledger of its own.
    internal sealed class FishingRewardPreparation
    {
        private readonly RuntimePlayerInventoryStore owner;
        private readonly ConnectionHandle connection;
        private readonly ulong serial;
        private readonly RuntimePlayerInventoryItem[] before;
        private readonly short[] quantity;
        private readonly short debitSlot;
        private readonly int debitType;
        private readonly long[] capturedCopper;
        private readonly ShopJournalCapture? sourceCapture;
        private readonly RuntimePlayerInventoryItem[] liveBefore;
        private long coinCredit;
        private long[]? copper;
        private bool debited, sealedPlan, adopted;
        internal RuntimePlayerInventoryItem[] Image { get; }
        internal RuntimePlayerInventoryMutation[] Mutations { get; private set; } = [];

        internal FishingRewardPreparation(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
            ulong serial, RuntimePlayerInventoryItem[] before, short[] quantity, short debitSlot, int debitType,
            long[] capturedCopper, ShopJournalCapture? sourceCapture, RuntimePlayerInventoryItem[] liveBefore)
        {
            this.owner = owner; this.connection = connection; this.serial = serial; this.before = before;
            this.quantity = quantity; this.debitSlot = debitSlot; this.debitType = debitType;
            this.capturedCopper = capturedCopper; this.sourceCapture = sourceCapture;
            this.liveBefore = liveBefore;
            Image = before.ToArray();
        }

        internal bool IsCurrent => !adopted && owner.TryIsCurrent(connection, serial) && sourceCapture?.IsCurrent != false;

        internal bool MatchesReports(ReadOnlySpan<RuntimePlayerInventoryItem> reports, ReadOnlySpan<bool> seen)
        {
            if (reports.Length < Image.Length || seen.Length < Image.Length) return false;
            for (int slot = 0; slot < Image.Length; slot++)
            {
                var expected = Image[slot];
                if (!debited && slot == debitSlot)
                    expected = expected.Stack == 1 ? default : expected with { Stack = (short)(expected.Stack - 1) };
                if ((seen[slot] && reports[slot] != expected) || (!seen[slot] && before[slot] != expected)) return false;
            }
            return true;
        }

        internal bool TryDebit()
        {
            if (sealedPlan || debited) return false;
            debited = true;
            if (debitSlot == -1) return true;
            var item = Image[debitSlot];
            if (item.ItemType.Value != debitType || item.Stack <= 0 || quantity[debitSlot] <= 0) return false;
            Image[debitSlot] = item.Stack == 1 ? default : item with { Stack = (short)(item.Stack - 1) };
            quantity[debitSlot]--;
            return true;
        }

        internal bool TryGrant(WorldItemPickupSlot1458 incoming, VanillaItemPrefixWorld1458? world, bool canGoIntoVoidVault,
            out WorldItemPickupSlot1458 residual)
        {
            residual = default;
            if (sealedPlan || !IsCurrent) return false;
            var slots = Image.Select(i => new WorldItemPickupSlot1458(i.ItemType.Value, i.Stack, i.Prefix.Value,
                (i.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)).ToArray();
            if (!VanillaWorldItemOwner1458.TryPlanStorage(in incoming, slots, false, world, out var storage) || storage is null)
                return false;
            // A represented open Void Bag may receive the remainder before overflow.
            // Unknown bank storage cannot be silently treated as full or empty.
            if (canGoIntoVoidVault && storage.Residual.Type != 0 && storage.Residual.Stack > 0 && Image.Any(i => i.ItemType.Value == 4131 && i.Stack > 0))
                return false;
            bool coin = incoming.Type is >= 71 and <= 74;
            if (coin) coinCredit = checked(coinCredit + CoinValue(new(new(incoming.Type), checked((short)storage.Taken), default, 0)));
            foreach (var mutation in storage.Mutations)
            {
                var next = mutation.After.Type == 0 ? default : new RuntimePlayerInventoryItem(new(mutation.After.Type),
                    checked((short)mutation.After.Stack), new(mutation.After.Prefix),
                    mutation.After.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
                if (!coin) quantity[mutation.Slot] = checked((short)(quantity[mutation.Slot] + next.Stack - Image[mutation.Slot].Stack));
                else quantity[mutation.Slot] = 0;
                Image[mutation.Slot] = next;
            }
            residual = storage.Residual;
            return true;
        }

        internal bool TrySeal()
        {
            if (sealedPlan || !debited || !IsCurrent || !AreValidFundedQuantities(quantity, Image)) return false;
            var changes = new List<RemotePhaseInventoryMutation>();
            for (short slot = 0; slot < before.Length; slot++)
                if (before[slot] != Image[slot] || liveBefore[slot] != Image[slot])
                    changes.Add(new(connection, slot, before[slot], Image[slot]));
            if (!TryPlanCoinFunding(before, changes.ToArray(), coinCredit, capturedCopper, out copper)) return false;
            Mutations = changes.Select(m => new RuntimePlayerInventoryMutation(m.Slot, m.After)).ToArray();
            sealedPlan = true;
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!sealedPlan || !IsCurrent || copper is null) return false;
            adopted = true;
            int player = connection.Player.Slot.Value;
            for (int slot = 0; slot < Image.Length; slot++)
            {
                owner.items[GetOffset(player, slot)] = Image[slot];
                int lane = QuantityLane(Image[slot].ItemType);
                if (lane >= 0) owner.ownedQuantityCaps[QuantityOffset(player, slot, lane)] = Image[slot].Stack;
                // The old identity's consumed cap also follows the accepted removal.
                int oldLane = QuantityLane(before[slot].ItemType);
                if (oldLane >= 0 && oldLane != lane) owner.ownedQuantityCaps[QuantityOffset(player, slot, oldLane)] = 0;
            }
            quantity.CopyTo(owner.fundedQuantities, player * InventorySlotCount);
            copper.CopyTo(owner.coinFundedValues, player * InventorySlotCount);
            owner.Changed(player);
            return true;
        }

        internal bool IsAcceptedItemCurrent(RuntimePlayerInventoryMutation mutation) => adopted &&
            owner.TryGet(connection, mutation.Slot, out var actual) && actual == mutation.Item;
    }
}
