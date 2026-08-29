using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Authoritative ordinary-inventory purchase transaction for runtime-defined NPC shops. The executor plans the
/// complete buyer inventory image first, commits every changed slot atomically, and only then emits packet-5 style
/// equipment events. Banks/provider currencies and arbitrary item stacking are intentionally separate slices.
/// </summary>
internal sealed class RuntimeNpcShopPurchaseExecutor
{
    // Terraria Player.inventory low layout: 0..49 ordinary carried inventory, 50..53 coin slots,
    // 54..57 ammo slots, 58 mouse item. Shop grants are restricted to the ordinary 0..49 span.
    private const int MainInventoryCount = 50;
    private const int OrdinaryInventoryCount = VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount;
    private const int CoinSlotStart = 50;
    private const int CoinSlotEndExclusive = 54;
    private const int AmmoSlotStart = 54;
    private const int AmmoSlotEndExclusive = 58;

    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeNpcArchetypeIdentityStore archetypes;
    private readonly RuntimeNpcShopCatalogRegistry shops;
    private readonly RuntimePlayerInventoryStore inventory;
    private readonly IRuntimePlayerEventSink? playerEvents;

    public RuntimeNpcShopPurchaseExecutor(
        RuntimeNpcStore npcs,
        RuntimeNpcArchetypeIdentityStore archetypes,
        RuntimeNpcShopCatalogRegistry shops,
        RuntimePlayerInventoryStore inventory,
        IRuntimePlayerEventSink? playerEvents = null)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(archetypes);
        ArgumentNullException.ThrowIfNull(shops);
        ArgumentNullException.ThrowIfNull(inventory);
        this.npcs = npcs;
        this.archetypes = archetypes;
        this.shops = shops;
        this.inventory = inventory;
        this.playerEvents = playerEvents;
    }

    public NpcShopPurchaseResult TryPurchase(
        ConnectionHandle buyerConnection,
        in NpcShopPurchaseRequest request)
    {
        if (!buyerConnection.IsAssigned || buyerConnection.Player != request.Buyer)
            return NpcShopPurchaseResult.InvalidBuyer;
        if (!request.Vendor.IsAssigned || !npcs.TryGet(request.Vendor, out _))
            return NpcShopPurchaseResult.InvalidVendor;
        if (!archetypes.TryGet(request.Vendor, out GameplayArchetypeId vendorArchetype))
            return NpcShopPurchaseResult.VendorHasNoArchetype;

        RuntimeNpcShopCatalogSnapshot snapshot = shops.Snapshot;
        if (!snapshot.TryGetById(request.ShopId, out NpcShopCatalog? catalog))
            return NpcShopPurchaseResult.ShopNotFound;
        if (catalog.NpcArchetypeId != vendorArchetype)
            return NpcShopPurchaseResult.VendorShopMismatch;
        if (!catalog.TryGetOffer(request.OfferId, out ShopOffer offer))
            return NpcShopPurchaseResult.OfferNotFound;
        if (offer.Currency != ShopCurrencyKind.VanillaCoins)
            return NpcShopPurchaseResult.UnsupportedCurrency;

        // Until TerraRuntime owns a source-backed maxStack definition catalog for arbitrary vanilla items, every
        // offer grants exactly one item. This is conservative for all item families and prevents impossible stacks.
        if (offer.Stack != 1)
            return NpcShopPurchaseResult.UnsupportedQuantity;

        Span<RuntimePlayerInventoryItem> original =
            stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if (!inventory.TryCopyInventory(buyerConnection, original))
            return NpcShopPurchaseResult.InvalidBuyer;

        Span<RuntimePlayerInventoryItem> working =
            stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        original.CopyTo(working);

        long totalCoinValue = 0;
        for (int slot = 0; slot < OrdinaryInventoryCount; slot++)
        {
            RuntimePlayerInventoryItem item = original[slot];
            if (item.IsEmpty || !VanillaCoinFacts.TryGetValue(item.ItemType, out long coinValue))
                continue;

            totalCoinValue = checked(totalCoinValue + checked(coinValue * item.Stack));
        }

        long price = offer.UnitPrice;
        if (totalCoinValue < price)
            return NpcShopPurchaseResult.InsufficientFunds;

        if (price > 0)
        {
            for (int slot = 0; slot < OrdinaryInventoryCount; slot++)
            {
                RuntimePlayerInventoryItem item = working[slot];
                if (!item.IsEmpty && VanillaCoinFacts.TryGetValue(item.ItemType, out _))
                    working[slot] = default;
            }
        }

        int destination = FindEmptyMainInventorySlot(working);
        if (destination < 0)
            return NpcShopPurchaseResult.InventoryFull;

        working[destination] = new RuntimePlayerInventoryItem(
            offer.ItemType,
            Stack: 1,
            Prefix: default,
            ItemFlags: 0);

        if (price > 0)
        {
            long change = totalCoinValue - price;
            if (!TryMaterializeChange(working, change))
                return NpcShopPurchaseResult.ChangeDoesNotFit;
        }

        Span<RuntimePlayerInventoryMutation> mutations =
            stackalloc RuntimePlayerInventoryMutation[VanillaPlayerItemSlotCatalog.InventoryCount];
        int mutationCount = 0;
        for (short slot = 0; slot < VanillaPlayerItemSlotCatalog.InventoryCount; slot++)
        {
            if (original[slot] == working[slot])
                continue;

            mutations[mutationCount++] = new RuntimePlayerInventoryMutation(slot, working[slot]);
        }

        ReadOnlySpan<RuntimePlayerInventoryMutation> committedMutations = mutations[..mutationCount];
        if (!inventory.TryApplyAtomic(buyerConnection, committedMutations))
            return NpcShopPurchaseResult.InventoryCommitRejected;

        for (int index = 0; index < committedMutations.Length; index++)
        {
            RuntimePlayerInventoryMutation mutation = committedMutations[index];
            PlayerEquipmentCommitRequest equipment =
                mutation.Item.ToCommitRequest(buyerConnection.Player.Slot, mutation.Slot);
            playerEvents?.PlayerEquipmentUpdated(buyerConnection, in equipment);
        }

        return NpcShopPurchaseResult.Committed;
    }

    private static int FindEmptyMainInventorySlot(Span<RuntimePlayerInventoryItem> inventory)
    {
        for (int slot = 0; slot < MainInventoryCount; slot++)
        {
            if (inventory[slot].IsEmpty)
                return slot;
        }

        return -1;
    }

    private static bool TryMaterializeChange(
        Span<RuntimePlayerInventoryItem> inventory,
        long value)
    {
        if (value < 0)
            return false;

        long platinum = value / VanillaCoinFacts.PlatinumValue;
        value %= VanillaCoinFacts.PlatinumValue;
        int gold = checked((int)(value / VanillaCoinFacts.GoldValue));
        value %= VanillaCoinFacts.GoldValue;
        int silver = checked((int)(value / VanillaCoinFacts.SilverValue));
        int copper = checked((int)(value % VanillaCoinFacts.SilverValue));

        while (platinum > 0)
        {
            short stack = checked((short)Math.Min(platinum, VanillaCoinFacts.PlatinumMaximumStack));
            if (!TryPlaceCoin(inventory, VanillaCoinFacts.PlatinumCoin, stack))
                return false;
            platinum -= stack;
        }

        if (gold > 0 && !TryPlaceCoin(inventory, VanillaCoinFacts.GoldCoin, checked((short)gold)))
            return false;
        if (silver > 0 && !TryPlaceCoin(inventory, VanillaCoinFacts.SilverCoin, checked((short)silver)))
            return false;
        if (copper > 0 && !TryPlaceCoin(inventory, VanillaCoinFacts.CopperCoin, checked((short)copper)))
            return false;

        return true;
    }

    private static bool TryPlaceCoin(
        Span<RuntimePlayerInventoryItem> inventory,
        ItemTypeId coin,
        short stack)
    {
        int slot = FindEmptyCoinDestination(inventory);
        if (slot < 0)
            return false;

        inventory[slot] = new RuntimePlayerInventoryItem(coin, stack, default, 0);
        return true;
    }

    private static int FindEmptyCoinDestination(Span<RuntimePlayerInventoryItem> inventory)
    {
        for (int slot = CoinSlotStart; slot < CoinSlotEndExclusive; slot++)
        {
            if (inventory[slot].IsEmpty)
                return slot;
        }

        for (int slot = 0; slot < MainInventoryCount; slot++)
        {
            if (inventory[slot].IsEmpty)
                return slot;
        }

        // Coins are valid Coin Gun ammunition, so the ammo span is a safe final destination for change.
        for (int slot = AmmoSlotStart; slot < AmmoSlotEndExclusive; slot++)
        {
            if (inventory[slot].IsEmpty)
                return slot;
        }

        return -1;
    }
}
