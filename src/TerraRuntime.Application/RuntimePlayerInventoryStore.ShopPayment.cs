using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // The quote caller authorizes stock/memo and the complete138 image batch.
    // This existing physical owner separately conserves provenance through each
    // bounded source operation, including transient buy then sale before one sync.
    internal bool TryPrepareShopPayment(ConnectionHandle connection, ShopWalletPlan1458 source,
        bool sale, int purchasedUnits, out ShopPaymentPreparation? preparation,
        RuntimePlayerMember? member = null, byte resolvedDiscountMask = 0)
    {
        var step = new ShopTransactionPlan1458(source, [], default, [], purchasedUnits,
            sale ? source.Copper : -source.Copper);
        return TryPrepareShopPaymentChain(connection, [step], out preparation, member, resolvedDiscountMask);
    }

    internal bool TryPrepareShopPaymentChain(ConnectionHandle connection,
        ReadOnlySpan<ShopTransactionPlan1458> steps, out ShopPaymentPreparation? preparation,
        RuntimePlayerMember? member = null, byte resolvedDiscountMask = 0, bool retired = false, byte resolvedArithmeticMask = 0, byte resolvedContextMask = 0)
    {
        preparation = null;
        if (steps.Length is < 1 or > 3 || !connection.IsAssigned ||
            connections[connection.Player.Slot.Value] != connection || Serial >= ulong.MaxValue - 1 ||
            (member is not null && (member.Connection != connection || member.ItemPhase is null ||
                resolvedDiscountMask is < 1 or > 3 || resolvedArithmeticMask > 3 || resolvedContextMask > 15 ||
                member.ItemPhase.Value.ClientUiPriceArithmeticMask > 3 ||
                (resolvedArithmeticMask != 0 && (resolvedArithmeticMask & member.ItemPhase.Value.CapturedClientUiPriceArithmeticMask) != resolvedArithmeticMask)))) return false;
        // Quote admission supplies a validated mask. Project its correlated phase
        // before either owner writes; adoption must contain no fallible projection.
        var beforePhase = member?.ItemPhase;
        RuntimePlayerItemPhase1458? afterPhase = beforePhase is { } phase
            ? resolvedContextMask != 0
                ? phase.ApplyAcceptedShopPriceContexts(resolvedContextMask, retired)
                : phase.ApplyAcceptedShopDiscount(resolvedDiscountMask, retired) : null;
        if (resolvedContextMask == 0 && afterPhase is { } projected && resolvedArithmeticMask != 0)
            afterPhase = projected.ApplyAcceptedClientUiPriceArithmetic(resolvedArithmeticMask);
        int player = connection.Player.Slot.Value;
        var before = new RuntimePlayerInventoryItem[InventorySlotCount + ShopBankImageCount];
        var beforeCopper = new long[before.Length];
        if (!TryCopyInventory(connection, before.AsSpan(0, InventorySlotCount)) ||
            !TryCaptureShopBanks(connection, before.AsSpan(InventorySlotCount), beforeCopper.AsSpan(InventorySlotCount))) return false;
        CaptureFundedCoinValues(connection).CopyTo(beforeCopper, 0);
        if (!TryCaptureShopBankOwnership(connection, out var bankQuantity, out var bankCaps)) return false;
        var beforeQuantity = CaptureFundedQuantities(connection);
        var beforeCaps = ownedQuantityCaps.AsSpan(player * InventorySlotCount * 5, InventorySlotCount * 5).ToArray();
        var physical = before;
        var copper = beforeCopper;
        var quantity = beforeQuantity.ToArray();
        var caps = beforeCaps.ToArray();
        foreach (var step in steps)
        {
            if (!TryPlanFundedShopStep(physical, copper, quantity, caps, step,
                    out var nextPhysical, out var nextCopper, out var nextQuantity, out var nextCaps)) return false;
            physical = nextPhysical; copper = nextCopper; quantity = nextQuantity; caps = nextCaps;
        }
        preparation = new(this, connection, playerMutationStamps[player], before, physical,
            beforeCopper, copper, beforeQuantity, quantity, beforeCaps, caps,
            member, beforePhase, afterPhase, bankQuantity, bankCaps);
        return true;
    }

    private static bool TryPlanFundedShopStep(RuntimePlayerInventoryItem[] before, long[] copper,
        short[] quantities, short[] caps, ShopTransactionPlan1458 step,
        out RuntimePlayerInventoryItem[] after, out long[] nextCopper,
        out short[] nextQuantity, out short[] nextCaps)
    {
        after = []; nextCopper = []; nextQuantity = []; nextCaps = [];
        var source = step.Wallet;
        if (source.Inventory.Length != InventorySlotCount || source.Banks.Length != ShopBankImageCount || source.Copper < 0)
            return false;
        after = new RuntimePlayerInventoryItem[before.Length];
        for (int i = 0; i < after.Length; i++)
        {
            var item = i < InventorySlotCount ? source.Inventory[i] : source.Banks[i - InventorySlotCount];
            if (item.Stack is < 0 or > short.MaxValue || item.Type is < 0 or > short.MaxValue ||
                item.Prefix is < 0 or > byte.MaxValue || (item.Type == 0 && item != default)) return false;
            after[i] = item.Type == 0 ? default : new(new(item.Type), (short)item.Stack,
                new(item.Prefix), item.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
            if (!after[i].IsCanonical) return false;
        }
        const int cursor = VanillaPlayerItemSlotCatalog.InventoryMouseItem;
        var oldCursor = before[cursor]; var nextCursor = after[cursor];
        if (!AreValidFundedQuantities(quantities, before.AsSpan(0, InventorySlotCount))) return false;
        for (int i = 0; i < before.Length; i++)
        {
            if (i == cursor) continue;
            if (i is >= 54 and < InventorySlotCount && before[i] != after[i]) return false;
            if (before[i] != after[i] && ((!before[i].IsEmpty && CoinValue(before[i]) == 0) ||
                (!after[i].IsEmpty && CoinValue(after[i]) == 0))) return false;
        }
        bool sale = !oldCursor.IsEmpty && nextCursor.IsEmpty;
        nextQuantity = quantities.ToArray(); nextCaps = caps.ToArray();
        if (sale)
        {
            if (CoinValue(oldCursor) != 0 || quantities[cursor] != oldCursor.Stack || step.CopperDelta != source.Copper)
                return false;
            nextQuantity[cursor] = 0;
        }
        else
        {
            if (step.Units <= 0 || nextCursor.IsEmpty || CoinValue(nextCursor) != 0 ||
                (!oldCursor.IsEmpty && (oldCursor.ItemType != nextCursor.ItemType || oldCursor.Prefix != nextCursor.Prefix)) ||
                nextCursor.Stack - Math.Max(0, (int)oldCursor.Stack) != step.Units || step.CopperDelta != -source.Copper)
                return false;
            nextQuantity[cursor] = checked((short)(quantities[cursor] + step.Units));
        }
        if (!TryPlanShopCopper(before, after, copper, sale ? 0 : source.Copper,
                sale ? source.Copper : 0, out nextCopper)) return false;
        int lane = QuantityLane(sale ? oldCursor.ItemType : nextCursor.ItemType);
        if (lane >= 0) nextCaps[cursor * 5 + lane] = sale ? (short)0 : nextCursor.Stack;
        return true;
    }

    // Hidden buy/sellback alternatives borrow Game's working images. This scratch
    // belongs to the same inventory owner and never publishes or changes live state.
    // Only the chosen detached plans may later create the ordinary payment token.
    internal ShopFundingComparison? TryCreateShopFundingComparison(ConnectionHandle connection)
    {
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection) return null;
        int player = connection.Player.Slot.Value;
        var before = new RuntimePlayerInventoryItem[InventorySlotCount + ShopBankImageCount];
        var copper = new long[before.Length];
        if (!TryCopyInventory(connection, before.AsSpan(0, InventorySlotCount)) ||
            !TryCaptureShopBanks(connection, before.AsSpan(InventorySlotCount), copper.AsSpan(InventorySlotCount)) ||
            !before[VanillaPlayerItemSlotCatalog.InventoryMouseItem].IsEmpty) return null;
        coinFundedValues.AsSpan(player * InventorySlotCount, InventorySlotCount).CopyTo(copper);
        var quantities = fundedQuantities.AsSpan(player * InventorySlotCount, InventorySlotCount).ToArray();
        if (!AreValidFundedQuantities(quantities, before.AsSpan(0, InventorySlotCount))) return null;
        return new(before, copper, quantities,
            ownedQuantityCaps.AsSpan(player * InventorySlotCount * 5, InventorySlotCount * 5).ToArray());
    }

    internal sealed class ShopFundingComparison(RuntimePlayerInventoryItem[] before, long[] beforeCopper,
        short[] quantities, short[] beforeCaps)
    {
        private readonly RuntimePlayerInventoryItem[] bought = new RuntimePlayerInventoryItem[before.Length];
        private readonly RuntimePlayerInventoryItem[] sold = new RuntimePlayerInventoryItem[before.Length];
        private readonly long[] boughtCopper = new long[before.Length];
        private readonly long[] soldCopper = new long[before.Length];
        private readonly long[] acceptedCopper = new long[before.Length];
        private readonly short[] candidateCaps = new short[beforeCaps.Length];
        private readonly short[] acceptedCaps = new short[beforeCaps.Length];
        private bool hasAccepted;

        // With every original wallet coin fully funded, payment and refund leave
        // every resulting coin fully funded. The bounded allocator cannot put
        // more credit in a slot than its physical copper capacity.
        internal bool IsFullyFundedWallet
        {
            get
            {
                for (int i = 0; i < before.Length; i++)
                    if (!(i is >= 54 and < InventorySlotCount) && beforeCopper[i] != CoinValue(before[i]))
                        return false;
                return true;
            }
        }

        internal bool TryCompare(ReadOnlySpan<WorldItemPickupSlot1458> boughtInventory,
            ReadOnlySpan<WorldItemPickupSlot1458> boughtBanks,
            ReadOnlySpan<WorldItemPickupSlot1458> soldInventory,
            ReadOnlySpan<WorldItemPickupSlot1458> soldBanks,
            int units, long charged, long granted, out bool sameAcceptedFunding)
        {
            sameAcceptedFunding = false;
            const int cursor = VanillaPlayerItemSlotCatalog.InventoryMouseItem;
            if (units <= 0 || units > short.MaxValue ||
                !CopyImage(boughtInventory, boughtBanks, bought) || !CopyImage(soldInventory, soldBanks, sold) ||
                bought[cursor].IsEmpty || CoinValue(bought[cursor]) != 0 || bought[cursor].Stack != units ||
                !sold[cursor].IsEmpty || quantities[cursor] != 0) return false;
            for (int i = 0; i < before.Length; i++)
            {
                if (i == cursor) continue;
                if ((i is >= 54 and < InventorySlotCount && (before[i] != bought[i] || before[i] != sold[i])) ||
                    ((before[i] != bought[i] || before[i] != sold[i]) &&
                    ((!before[i].IsEmpty && CoinValue(before[i]) == 0) ||
                     (!bought[i].IsEmpty && CoinValue(bought[i]) == 0) ||
                     (!sold[i].IsEmpty && CoinValue(sold[i]) == 0)))) return false;
            }
            if (!TryPlanShopCopper(before, bought, beforeCopper, charged, 0, boughtCopper.AsSpan()) ||
                !TryPlanShopCopper(bought, sold, boughtCopper, 0, granted, soldCopper.AsSpan())) return false;
            beforeCaps.CopyTo(candidateCaps, 0);
            int lane = QuantityLane(bought[cursor].ItemType);
            if (lane >= 0) candidateCaps[cursor * 5 + lane] = 0;
            if (!hasAccepted)
            {
                soldCopper.CopyTo(acceptedCopper, 0); candidateCaps.CopyTo(acceptedCaps, 0);
                hasAccepted = true; sameAcceptedFunding = true;
            }
            else sameAcceptedFunding = soldCopper.AsSpan().SequenceEqual(acceptedCopper) &&
                candidateCaps.AsSpan().SequenceEqual(acceptedCaps);
            return true;
        }

        internal bool MatchesAccepted(ShopPaymentPreparation preparation) =>
            preparation.MatchesBorrowedFunding(soldCopper, quantities, candidateCaps);

        private static bool CopyImage(ReadOnlySpan<WorldItemPickupSlot1458> main,
            ReadOnlySpan<WorldItemPickupSlot1458> banks, Span<RuntimePlayerInventoryItem> destination)
        {
            if (main.Length != InventorySlotCount || banks.Length != ShopBankImageCount) return false;
            for (int i = 0; i < destination.Length; i++)
            {
                var item = i < InventorySlotCount ? main[i] : banks[i - InventorySlotCount];
                if (item.Type is < 0 or > short.MaxValue || item.Stack is < 0 or > short.MaxValue ||
                    item.Prefix is < 0 or > byte.MaxValue || (item.Type == 0 && item != default)) return false;
                destination[i] = item.Type == 0 ? default : new(new(item.Type), (short)item.Stack,
                    new(item.Prefix), item.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
                if (!destination[i].IsCanonical) return false;
            }
            return true;
        }
    }

    internal sealed class ShopPaymentPreparation(RuntimePlayerInventoryStore owner,
        ConnectionHandle connection, ulong stamp, RuntimePlayerInventoryItem[] before,
        RuntimePlayerInventoryItem[] after, long[] beforeCopper, long[] afterCopper,
        short[] beforeQuantity, short[] afterQuantity, short[] beforeCaps, short[] afterCaps,
        RuntimePlayerMember? member, RuntimePlayerItemPhase1458? beforePhase, RuntimePlayerItemPhase1458? afterPhase, short[] bankQuantity, short[] bankCaps)
    {
        private bool adopted;
        private RuntimePlayerInventoryItem[] AcceptedItems => after;
        private long[] AcceptedCopper => afterCopper;
        private short[] AcceptedQuantity => afterQuantity;
        private short[] AcceptedCaps => afterCaps;
        internal bool MatchesBorrowedFunding(ReadOnlySpan<long> copper, ReadOnlySpan<short> quantity,
            ReadOnlySpan<short> caps) => afterCopper.AsSpan().SequenceEqual(copper) &&
                afterQuantity.AsSpan().SequenceEqual(quantity) && afterCaps.AsSpan().SequenceEqual(caps);

        internal bool HasSameAcceptedImage(ShopPaymentPreparation other) =>
            after.AsSpan().SequenceEqual(other.AcceptedItems) &&
            afterCopper.AsSpan().SequenceEqual(other.AcceptedCopper) &&
            afterQuantity.AsSpan().SequenceEqual(other.AcceptedQuantity) &&
            afterCaps.AsSpan().SequenceEqual(other.AcceptedCaps);

        internal bool IsCurrent
        {
            get
            {
                if ((member is not null && member.ItemPhase != beforePhase) || adopted || !owner.IsCurrentCursorInventory(connection, stamp) || owner.Serial >= ulong.MaxValue - 1)
                    return false;
                int player = connection.Player.Slot.Value;
                return owner.items.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual(before.AsSpan(0, InventorySlotCount)) &&
                    owner.shopBankItems.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(before.AsSpan(InventorySlotCount)) &&
                    owner.coinFundedValues.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual(beforeCopper.AsSpan(0, InventorySlotCount)) &&
                    owner.shopBankFundedCopper.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(beforeCopper.AsSpan(InventorySlotCount)) &&
                    owner.fundedQuantities.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual(beforeQuantity) &&
                    owner.ownedQuantityCaps.AsSpan(player * InventorySlotCount * 5, InventorySlotCount * 5).SequenceEqual(beforeCaps) &&
                    owner.shopBankFundedQuantities.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(bankQuantity) &&
                    owner.shopBankQuantityCaps.AsSpan(player * ShopBankImageCount * 5, ShopBankImageCount * 5).SequenceEqual(bankCaps);
            }
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            adopted = true;
            int player = connection.Player.Slot.Value;
            after.AsSpan(0, InventorySlotCount).CopyTo(owner.items.AsSpan(player * InventorySlotCount, InventorySlotCount));
            after.AsSpan(InventorySlotCount).CopyTo(owner.shopBankItems.AsSpan(player * ShopBankImageCount, ShopBankImageCount));
            afterCopper.AsSpan(0, InventorySlotCount).CopyTo(owner.coinFundedValues.AsSpan(player * InventorySlotCount, InventorySlotCount));
            afterCopper.AsSpan(InventorySlotCount).CopyTo(owner.shopBankFundedCopper.AsSpan(player * ShopBankImageCount, ShopBankImageCount));
            afterQuantity.CopyTo(owner.fundedQuantities, player * InventorySlotCount);
            afterCaps.CopyTo(owner.ownedQuantityCaps, player * InventorySlotCount * 5);
            if (member is not null) member.ItemPhase = afterPhase;
            owner.Changed(player);
            return true;
        }

        internal bool IsAcceptedSlotCurrent(short protocolSlot)
        {
            if (!adopted || !connection.IsAssigned || owner.connections[connection.Player.Slot.Value] != connection)
                return false;
            int player = connection.Player.Slot.Value;
            if (VanillaPlayerItemSlotCatalog.IsInventorySlot(protocolSlot))
            {
                int offset = GetOffset(player, protocolSlot);
                return owner.items[offset] == after[protocolSlot] && owner.coinFundedValues[offset] == afterCopper[protocolSlot] &&
                    owner.fundedQuantities[offset] == afterQuantity[protocolSlot] &&
                    owner.ownedQuantityCaps.AsSpan(offset * 5, 5).SequenceEqual(afterCaps.AsSpan(protocolSlot * 5, 5));
            }
            if (!TryGetShopBankOffset(protocolSlot, out int bank)) return false;
            int bankOffset = player * ShopBankImageCount + bank;
            return owner.shopBankItems[bankOffset] == after[InventorySlotCount + bank] &&
                owner.shopBankFundedCopper[bankOffset] == afterCopper[InventorySlotCount + bank] &&
                owner.shopBankFundedQuantities[bankOffset] == bankQuantity[bank] &&
                owner.shopBankQuantityCaps.AsSpan(bankOffset * 5, 5).SequenceEqual(bankCaps.AsSpan(bank * 5, 5));
        }
    }
}
