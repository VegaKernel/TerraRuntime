using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct VanillaVoidStorageFacts1458(
    bool WorksInVoidBag,
    bool RefreshesInformation,
    bool RefreshesMechanical)
{
    public bool IsPhaseNeutral => !WorksInVoidBag || (!RefreshesInformation && !RefreshesMechanical);
}

/// <summary>Official 1.4.5.8 stored-bank role, not ordinary inventory or equipment admission.</summary>
public static class VanillaVoidStorageCatalog1458
{
    // Numeric metadata independently captured from ItemID.Sets and the two original
    // Player.Refresh*FromItemType methods; no armor or prefix grants run for this role.
    private static ReadOnlySpan<int> InformationWriters => [15, 16, 17, 18, 393, 395, 707, 708, 709, 3036, 3037, 3084, 3095, 3096, 3099, 3102, 3118, 3119, 3120, 3121, 3122, 3123, 3124, 5358, 5359, 5360, 5361, 5574, 5575, 5576];
    private static ReadOnlySpan<int> MechanicalWriters => [486, 2216, 2799, 3061, 3611, 3619, 3624, 4346, 4767, 5095, 5126, 5309, 5323, 6190];
    private static ReadOnlySpan<int> ExcludedFromVoidBag => [4346, 5095];

    public static bool TryGet(ItemTypeId item, out VanillaVoidStorageFacts1458 facts)
    {
        facts = default;
        if ((uint)item.Value >= VanillaItemIds.Count)
            return false;

        facts = new(!ExcludedFromVoidBag.Contains(item.Value),
            InformationWriters.Contains(item.Value), MechanicalWriters.Contains(item.Value));
        return true;
    }

    public static bool IsPhaseNeutral(ItemTypeId item) => TryGet(item, out var facts) && facts.IsPhaseNeutral;

    /// <summary>
    /// Type-only source gate when scanning ordinary main slots 0..57. The cursor and
    /// bank contents do not open storage; raw zero-stack setup is distinct from runtime canonicality.
    /// </summary>
    public static bool OpensVoidStorage(ItemTypeId item) => item.Value == OpenVoidBag;

    private const int OpenVoidBag = 4131;
}
