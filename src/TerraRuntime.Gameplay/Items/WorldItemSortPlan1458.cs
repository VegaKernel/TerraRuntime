namespace TerraRuntime.Gameplay.Items;

/// <summary>Complete mutable-slot bijection after ordered merge/coin/ammo steps.
/// Main slots use 0..58; bank slots use 59..98, matching BankStorageStep1458.
/// Before[i] is the exact pre-permutation image at Sources[i].</summary>
public sealed record WorldItemSortPermutation1458(short[] Sources,
    short[] Destinations, WorldItemPickupSlot1458[] Before);

/// <summary>Detached original sorting image and operation journal; no funding is assigned.</summary>
public sealed record WorldItemSortPlan1458(WorldItemPickupSlot1458[] Slots,
    BankStorageStep1458[] Steps, WorldItemSortPermutation1458 Permutation,
    WorldItemPickupSlot1458[]? Overflow = null);
