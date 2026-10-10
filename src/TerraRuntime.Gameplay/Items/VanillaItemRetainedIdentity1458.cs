using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// The retained type produced by original Terraria 1.4.5.8 Item.SetDefaults for canonical requested identities.
/// This query owns only positive identity remaps and deprecated Air, not item defaults, stacks, prefixes,
/// signed net-id aliases or the subsequent packet-5 encoder's empty-item cleanup.
/// </summary>
public static class VanillaItemRetainedIdentity1458
{
    public static bool TryResolve(ItemTypeId requested, out ItemTypeId retained)
    {
        retained = default;
        if (requested.Value >= VanillaItemIds.Count)
            return false;

        // Original Terraria 1.4.5.8 Item.SetDefaults type replacements and ItemID.Sets.Deprecated.
        // Numeric fixture item-retained-identity-official-1458.json independently records all 6,196
        // canonical requests; this query does not model the remaining SetDefaults fields.
        int value = requested.Value switch
        {
            226 => 227,
            1803 => 1533,
            1804 => 1534,
            1805 => 1535,
            1806 => 1536,
            1807 => 1537,
            2772 or 2773 or 2775 or 2777 or 2778 or 2780 or 2782 or 2783 or 2785 or
            2881 or 3462 or 3463 or 3465 or 3847 or 3848 or 3849 or 3850 or 3851 or
            3861 or 3862 or 3978 or 4010 or 4058 or 4722 or 6143 or 6160 or 6170 or 6171 => 0,
            _ => requested.Value
        };
        retained = new ItemTypeId(value);
        return true;
    }
}
