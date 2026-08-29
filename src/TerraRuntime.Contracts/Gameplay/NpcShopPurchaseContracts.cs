using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Stable purchase identity. The buyer and vendor are exact-generation runtime handles; catalog/offer identity is
/// stable control-plane metadata and never depends on presentation order.
/// </summary>
public readonly record struct NpcShopPurchaseRequest(
    PlayerHandle Buyer,
    NpcHandle Vendor,
    ShopId ShopId,
    ShopOfferId OfferId);

/// <summary>
/// Immutable post-commit observation of an authoritative shop purchase.
/// </summary>
public readonly record struct NpcShopPurchaseCommit(
    PlayerHandle Buyer,
    NpcHandle Vendor,
    ShopId ShopId,
    ShopOfferId OfferId,
    ItemTypeId ItemType,
    short Stack,
    ShopCurrencyKind Currency,
    long Price,
    long Change,
    ulong CatalogRevision,
    short DestinationSlot,
    int InventoryMutationCount);

/// <summary>
/// Observer boundary invoked only after the complete inventory transaction commits.
/// </summary>
public interface INpcShopPurchaseCommitSink
{
    void PurchaseCommitted(in NpcShopPurchaseCommit purchase);
}

public enum NpcShopPurchaseResult : byte
{
    Committed = 0,
    InvalidBuyer = 1,
    InvalidVendor = 2,
    VendorHasNoArchetype = 3,
    ShopNotFound = 4,
    VendorShopMismatch = 5,
    OfferNotFound = 6,
    UnsupportedCurrency = 7,
    UnsupportedQuantity = 8,
    InvalidCurrencyState = 9,
    InsufficientFunds = 10,
    InventoryFull = 11,
    ChangeDoesNotFit = 12,
    InventoryCommitRejected = 13
}
