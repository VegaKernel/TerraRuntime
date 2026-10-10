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
/// Bounded explicit-prefix normalization over source-pinned SetDefaults acceptance metadata.
/// This operation does not materialize item stats or admit the resulting item to combat/item use.
/// </summary>
/// <remarks>
/// Reimplements the nonnegative request path of original 1.4.5.8 <c>Item.Prefix</c>.
/// The existing prefix table supplies numerical acceptance and natural family order.
/// Unknown world context excludes nonzero requests on original <c>ItemVariants</c> identities;
/// it retains 868 invariant prefix-capable records. Known context selects the verified variant
/// acceptance fields over the existing 886-record normal table, without claiming full item defaults.
/// The caller supplies a detached cursor and adopts it only when the result is Resolved.
/// </remarks>
public static class VanillaItemPrefixNormalization1458
{
    // Original Prefix has no iteration cap. Exhaustion is selective refusal; the caller
    // must discard the planned cursor and never adopt a partial resolution.
    public const int MaximumAttempts = 256;

    /// <summary>Classifies canonical inputs and required world provenance before preparing a cursor.</summary>
    public static bool IsSupported(ItemTypeId item, PrefixId requested, VanillaItemPrefixWorld1458? world = null) =>
        (uint)item.Value < VanillaItemIds.Count &&
        (uint)requested.Value < VanillaItemPrefixTable1458.PrefixCount &&
        (requested.Value == 0 || !PrefixWorldRecords1458.HasVariant(item.Value) || world.HasValue);

    public static VanillaItemPrefixNormalizationStatus1458 Resolve(
        ItemTypeId item,
        PrefixId requested,
        Func<int, int> next,
        bool windowsItemPrefixArithmetic,
        out VanillaItemPrefixResolution1458 resolution,
        VanillaItemPrefixWorld1458? world = null)
    {
        resolution = default;
        ArgumentNullException.ThrowIfNull(next);
        if (!IsSupported(item, requested, world))
            return VanillaItemPrefixNormalizationStatus1458.Unsupported;

        // Original request zero returns before CanHavePrefixes/default acceptance.
        if (requested.Value == 0)
            return VanillaItemPrefixNormalizationStatus1458.Resolved;

        bool prefixable = PrefixWorldRecords1458.HasVariant(item.Value)
            ? PrefixWorldRecords1458.TryGet(item, world!.Value, out var record)
            : VanillaItemPrefixTable1458.TryGet(item, out record);
        if (!prefixable)
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

}
