using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Resolved prefix byte and the original Prefix call's applied/no-prefix distinction.</summary>
public readonly record struct VanillaItemPrefixResolution1458(PrefixId Prefix, bool Applied);

public enum VanillaItemPrefixNormalizationStatus1458
{
    Unsupported,
    Resolved,
    BudgetExhausted,
    InvalidDraw
}

/// <summary>
/// Bounded explicit-prefix normalization over invariant original SetDefaults metadata.
/// This operation does not materialize item stats or admit the resulting item to combat/item use.
/// </summary>
/// <remarks>
/// Reimplements the nonnegative request path of original 1.4.5.8 <c>Item.Prefix</c>.
/// The existing prefix table supplies numerical acceptance and natural family order.
/// All world-selected IDs from original <c>ItemVariants</c> are excluded for nonzero requests;
/// this leaves 868 invariant prefix-capable records without claiming full item defaults.
/// The caller supplies a detached cursor and adopts it only when the result is Resolved.
/// </remarks>
public static class VanillaItemPrefixNormalization1458
{
    // Original Prefix has no iteration cap. Exhaustion is selective refusal; the caller
    // must discard the planned cursor and never adopt a partial resolution.
    public const int MaximumAttempts = 256;

    /// <summary>Classifies invariant canonical inputs before the caller prepares a cursor.</summary>
    public static bool IsSupported(ItemTypeId item, PrefixId requested) =>
        (uint)item.Value < VanillaItemIds.Count &&
        (uint)requested.Value < VanillaItemPrefixTable1458.PrefixCount &&
        (requested.Value == 0 || !HasWorldVariant(item));

    public static VanillaItemPrefixNormalizationStatus1458 Resolve(
        ItemTypeId item,
        PrefixId requested,
        Func<int, int> next,
        bool windowsItemPrefixArithmetic,
        out VanillaItemPrefixResolution1458 resolution)
    {
        resolution = default;
        ArgumentNullException.ThrowIfNull(next);
        if (!IsSupported(item, requested))
            return VanillaItemPrefixNormalizationStatus1458.Unsupported;

        // Original request zero returns before CanHavePrefixes/default acceptance.
        if (requested.Value == 0)
            return VanillaItemPrefixNormalizationStatus1458.Resolved;

        if (!VanillaItemPrefixTable1458.TryGet(item, out var record))
            return VanillaItemPrefixNormalizationStatus1458.Resolved;

        int candidate = requested.Value;
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            if (candidate < 0)
            {
                int offer = next(4);
                if ((uint)offer >= 4)
                    return VanillaItemPrefixNormalizationStatus1458.InvalidDraw;
                if (offer == 0)
                    candidate = 0;
                else
                {
                    ReadOnlySpan<byte> family = VanillaItemPrefixTable1458.GetFamily(record.Family);
                    int index = next(family.Length);
                    if ((uint)index >= (uint)family.Length)
                        return VanillaItemPrefixNormalizationStatus1458.InvalidDraw;
                    candidate = family[index];
                }
            }

            // Explicit numeric survivability does not require rollable family membership.
            // ReducedNaturalChance is absent: the original requested prefix is nonnegative.
            if (VanillaItemPrefixTable1458.Accepts(in record, candidate, windowsItemPrefixArithmetic))
            {
                resolution = new(new PrefixId(candidate), Applied: true);
                return VanillaItemPrefixNormalizationStatus1458.Resolved;
            }
            candidate = -1;
        }
        return VanillaItemPrefixNormalizationStatus1458.BudgetExhausted;
    }

    // Original IDs with world-selected variants: their normal-world row does not
    // prove current numerical acceptance. All variants are conservatively excluded.
    private static bool HasWorldVariant(ItemTypeId item) => item.Value is
        112 or 157 or 197 or 517 or 544 or 556 or 557 or 683 or 725 or
        1314 or 1319 or 1325 or 2623 or 3069 or 4060 or 5147 or
        5279 or 5280 or 5281 or 5282 or 5283 or 5284 or 5334 or 5687 or 5688;
}
