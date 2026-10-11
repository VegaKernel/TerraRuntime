using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    private const int ShopBankCount = 4;
    private const int ShopBankSlots = 40;
    private const int ShopBankImageCount = ShopBankCount * ShopBankSlots;
    private readonly RuntimePlayerInventoryItem[] shopBankItems =
        new RuntimePlayerInventoryItem[PlayerSlotCount * ShopBankImageCount];
    private readonly long[] shopBankFundedCopper = new long[PlayerSlotCount * ShopBankImageCount];
    private readonly short[] shopBankFundedQuantities = new short[PlayerSlotCount * ShopBankImageCount];
    private readonly short[] shopBankQuantityCaps = CreateShopBankQuantityCaps();
    private static short[] CreateShopBankQuantityCaps()
    {
        var result = new short[PlayerSlotCount * ShopBankImageCount * 5];
        Array.Fill(result, (short)-1);
        return result;
    }

    internal bool TryCaptureShopBankOwnership(ConnectionHandle connection,
        out short[] quantities, out short[] caps)
    {
        quantities = []; caps = [];
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection) return false;
        int player = connection.Player.Slot.Value;
        quantities = shopBankFundedQuantities.AsSpan(player * ShopBankImageCount, ShopBankImageCount).ToArray();
        caps = shopBankQuantityCaps.AsSpan(player * ShopBankImageCount * 5, ShopBankImageCount * 5).ToArray();
        return AreValidShopBankOwnership(shopBankItems.AsSpan(player * ShopBankImageCount, ShopBankImageCount), quantities, caps);
    }

    internal static bool AreValidShopBankOwnership(ReadOnlySpan<RuntimePlayerInventoryItem> image,
        ReadOnlySpan<short> quantities, ReadOnlySpan<short> caps)
    {
        if (image.Length != ShopBankImageCount || quantities.Length != ShopBankImageCount ||
            caps.Length != ShopBankImageCount * 5) return false;
        for (int i = 0; i < image.Length; i++)
        {
            if (quantities[i] < 0 || quantities[i] > Math.Max(0, (int)image[i].Stack) ||
                (CoinValue(image[i]) != 0 && quantities[i] != 0)) return false;
            for (int lane = 0; lane < 5; lane++)
            {
                int cap = caps[i * 5 + lane];
                if (cap < -1 || (cap >= 0 && !IsValidOwnedQuantityMaximum(new(OwnedQuantityTypes[lane]), cap)) ||
                    (QuantityLane(image[i].ItemType) == lane && cap >= 0 && image[i].Stack > cap)) return false;
            }
        }
        return true;
    }

    // SlotID reserves200 identifiers per bank. Only its actual40 source items exist.
    internal static bool TryGetShopBankOffset(short protocolSlot, out int offset)
    {
        offset = -1;
        ReadOnlySpan<short> starts = [99, 299, 500, 700];
        for (int bank = 0; bank < starts.Length; bank++)
        {
            int slot = protocolSlot - starts[bank];
            if ((uint)slot < ShopBankSlots)
            {
                offset = bank * ShopBankSlots + slot;
                return true;
            }
        }
        return false;
    }

    internal bool TryCaptureShopBanks(ConnectionHandle connection,
        Span<RuntimePlayerInventoryItem> image, Span<long> fundedCopper)
    {
        if (image.Length != ShopBankImageCount || fundedCopper.Length != ShopBankImageCount ||
            !connection.IsAssigned || connections[connection.Player.Slot.Value] != connection)
            return false;
        int offset = connection.Player.Slot.Value * ShopBankImageCount;
        shopBankItems.AsSpan(offset, ShopBankImageCount).CopyTo(image);
        shopBankFundedCopper.AsSpan(offset, ShopBankImageCount).CopyTo(fundedCopper);
        return AreValidShopBanks(image, fundedCopper);
    }

    internal static bool AreValidShopBanks(ReadOnlySpan<RuntimePlayerInventoryItem> image,
        ReadOnlySpan<long> fundedCopper)
    {
        if (image.Length != ShopBankImageCount || fundedCopper.Length != ShopBankImageCount)
            return false;
        for (int slot = 0; slot < image.Length; slot++)
        {
            var item = image[slot];
            if (!item.IsCanonical || (item.IsEmpty && item != default) || (item.ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0 ||
                fundedCopper[slot] < 0 || fundedCopper[slot] > CoinValue(item))
                return false;
        }
        return true;
    }

    // The caller first owns retained identity/prefix normalization. This receives its
    // canonical image; it never grants copper or treats a client bank report as payment.
    internal bool CanSetShopBankReport(ConnectionHandle connection, short protocolSlot,
        in RuntimePlayerInventoryItem after)
    {
        if (!connection.IsAssigned || !CanAccept(connection) || Serial >= ulong.MaxValue - 1 ||
            !TryGetShopBankOffset(protocolSlot, out int bankSlot) || !after.IsCanonical ||
            (after.ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) return false;
        int offset = connection.Player.Slot.Value * ShopBankImageCount + bankSlot;
        int lane = QuantityLane(after.ItemType);
        return lane < 0 || shopBankQuantityCaps[offset * 5 + lane] < 0 ||
            after.Stack <= shopBankQuantityCaps[offset * 5 + lane];
    }

    internal bool TrySetShopBankReport(ConnectionHandle connection, short protocolSlot,
        in RuntimePlayerInventoryItem after)
    {
        if (!CanSetShopBankReport(connection, protocolSlot, in after) ||
            !TryGetShopBankOffset(protocolSlot, out int bankSlot)) return false;
        int player = connection.Player.Slot.Value;
        int offset = player * ShopBankImageCount + bankSlot;
        var before = shopBankItems[offset];
        long retained = before.ItemType == after.ItemType && before.Prefix == after.Prefix
            ? Math.Min(shopBankFundedCopper[offset], CoinValue(after)) : 0;
        connections[player] = connection;
        shopBankItems[offset] = after;
        shopBankFundedCopper[offset] = retained;
        shopBankFundedQuantities[offset] = before.ItemType == after.ItemType && before.Prefix == after.Prefix && CoinValue(after) == 0
            ? (short)Math.Min(shopBankFundedQuantities[offset], Math.Max(0, (int)after.Stack)) : (short)0;
        Changed(player);
        return true;
    }

    private void ClearShopBanks(int player)
    {
        shopBankItems.AsSpan(player * ShopBankImageCount, ShopBankImageCount).Clear();
        shopBankFundedCopper.AsSpan(player * ShopBankImageCount, ShopBankImageCount).Clear();
        shopBankFundedQuantities.AsSpan(player * ShopBankImageCount, ShopBankImageCount).Clear();
        shopBankQuantityCaps.AsSpan(player * ShopBankImageCount * 5, ShopBankImageCount * 5).Fill(-1);
    }

    // Transfer invokes this after all tuple/value validation and counter reservation.
    // It is the same physical owner; no receipt or speculative economic credit travels.
    internal bool TryAdoptShopBanks(ConnectionHandle connection,
        ReadOnlySpan<RuntimePlayerInventoryItem> image, ReadOnlySpan<long> fundedCopper,
        ReadOnlySpan<short> quantities = default, ReadOnlySpan<short> caps = default)
    {
        if (quantities.IsEmpty) quantities = new short[ShopBankImageCount];
        if (caps.IsEmpty) { var unknown = new short[ShopBankImageCount * 5]; Array.Fill(unknown, (short)-1); caps = unknown; }
        if (!AreValidShopBankOwnership(image, quantities, caps)) return false;
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection ||
            Serial >= ulong.MaxValue - 1 || !AreValidShopBanks(image, fundedCopper))
            return false;
        int player = connection.Player.Slot.Value;
        image.CopyTo(shopBankItems.AsSpan(player * ShopBankImageCount, ShopBankImageCount));
        fundedCopper.CopyTo(shopBankFundedCopper.AsSpan(player * ShopBankImageCount, ShopBankImageCount));
        quantities.CopyTo(shopBankFundedQuantities.AsSpan(player * ShopBankImageCount, ShopBankImageCount));
        caps.CopyTo(shopBankQuantityCaps.AsSpan(player * ShopBankImageCount * 5, ShopBankImageCount * 5));
        Changed(player);
        return true;
    }

    // Runtime provenance is privately fungible across the same five source wallets.
    // This helper receives a separately authorized physical source plan. It cannot
    // authorize an offer, price, item grant, sale, or a raw client money image.
    internal static bool TryPlanShopCopper(ReadOnlySpan<RuntimePlayerInventoryItem> before219,
        ReadOnlySpan<RuntimePlayerInventoryItem> after219, ReadOnlySpan<long> funded219,
        long charged, long credited, out long[] afterFunding)
    {
        afterFunding = [];
        var output = new long[InventorySlotCount + ShopBankImageCount];
        if (!TryPlanShopCopper(before219, after219, funded219, charged, credited, output.AsSpan())) return false;
        afterFunding = output;
        return true;
    }

    internal static bool TryPlanShopCopper(ReadOnlySpan<RuntimePlayerInventoryItem> before219,
        ReadOnlySpan<RuntimePlayerInventoryItem> after219, ReadOnlySpan<long> funded219,
        long charged, long credited, Span<long> afterFunding)
    {
        int count = InventorySlotCount + ShopBankImageCount;
        if (before219.Length != count || after219.Length != count || funded219.Length != count || afterFunding.Length != count ||
            charged < 0 || credited < 0 || (charged != 0 && credited != 0)) return false;
        funded219.CopyTo(afterFunding);
        var next = afterFunding;
        long oldBudget = 0, beforeValue = 0, afterValue = 0;
        for (int i = 0; i < count; i++)
        {
            if (!before219[i].IsCanonical || !after219[i].IsCanonical ||
                funded219[i] < 0 || funded219[i] > CoinValue(before219[i])) return false;
            // BuyItem excludes ammo54..57 and mouse58 from the payment wallet.
            if (i is >= 54 and < InventorySlotCount)
            {
                if (funded219[i] > CoinValue(after219[i]) ||
                    (funded219[i] != 0 && (before219[i].ItemType != after219[i].ItemType ||
                                         before219[i].Prefix != after219[i].Prefix))) return false;
                continue;
            }
            oldBudget = checked(oldBudget + funded219[i]);
            beforeValue = checked(beforeValue + CoinValue(before219[i]));
            afterValue = checked(afterValue + CoinValue(after219[i]));
            next[i] = before219[i].ItemType == after219[i].ItemType && before219[i].Prefix == after219[i].Prefix
                ? Math.Min(funded219[i], CoinValue(after219[i])) : 0;
        }
        if (charged > oldBudget || credited > afterValue || checked(beforeValue - charged + credited) != afterValue)
            return false;
        long target = checked(oldBudget - charged + credited);
        long retained = 0;
        for (int i = 0; i < count; i++)
            if (!(i is >= 54 and < InventorySlotCount)) retained = checked(retained + next[i]);
        long debit = Math.Max(0, retained - target);
        for (int i = 0; i < count && debit != 0; i++)
        {
            if (i is >= 54 and < InventorySlotCount) continue;
            long amount = Math.Min(next[i], debit);
            next[i] -= amount;
            debit -= amount;
        }
        long pool = Math.Max(0, target - retained);
        for (int i = 0; i < count && pool != 0; i++)
        {
            if (i is >= 54 and < InventorySlotCount) continue;
            long amount = Math.Min(pool, CoinValue(after219[i]) - next[i]);
            next[i] += amount;
            pool -= amount;
        }
        if (debit != 0 || pool != 0) return false;
        long final = 0;
        for (int i = 0; i < count; i++)
            if (!(i is >= 54 and < InventorySlotCount)) final = checked(final + next[i]);
        if (final != target) return false;
        return true;
    }
}
