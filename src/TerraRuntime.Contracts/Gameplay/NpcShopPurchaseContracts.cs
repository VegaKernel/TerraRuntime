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
