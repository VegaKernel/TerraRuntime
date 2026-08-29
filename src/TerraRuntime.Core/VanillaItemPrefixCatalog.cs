using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>Source-backed vanilla prefix families currently needed by authoritative gameplay.</summary>
public enum VanillaItemPrefixFamily : byte
{
    None = 0,
    Summon = 1
}

/// <summary>
/// Sparse TerrariaServer 1.4.5.8 prefix catalog. Numeric IDs are version data and are kept here rather than
/// leaking through loot/world-item orchestration. Absence means unverified, not impossible in vanilla.
/// </summary>
public static class VanillaItemPrefixCatalog
{
    private static readonly PrefixId[] SummonPrefixes =
    [
        new(85), new(86), new(87), new(88), new(89), new(90), new(91), new(92), new(93), new(94), new(95),
        new(96), new(97), new(55), new(38), new(54), new(53), new(57), new(40), new(56), new(41), new(39)
    ];

    public static ReadOnlySpan<PrefixId> GetRollablePrefixes(VanillaItemPrefixFamily family) =>
        family == VanillaItemPrefixFamily.Summon
            ? SummonPrefixes
            : ReadOnlySpan<PrefixId>.Empty;

    public static bool HasReducedNaturalChance(PrefixId prefix) =>
        prefix.Value is 7 or 8 or 9 or 10 or 11 or 22 or 23 or 24 or 29 or 30 or 31 or
            39 or 40 or 41 or 47 or 48 or 49 or 56;

    /// <summary>
    /// Item-specific prefix validity after Terraria's stat-rounding guards. The current catalog only claims
    /// exact knowledge for Slime Staff. Prefix zero is the valid no-prefix result of natural rolling.
    /// </summary>
    public static bool IsValidForItem(ItemTypeId itemType, PrefixId prefix)
    {
        if (itemType != VanillaItemIds.SlimeStaff)
            return false;

        if (prefix.Value == 0)
            return true;

        if (!Contains(GetRollablePrefixes(VanillaItemPrefixFamily.Summon), prefix))
            return false;

        // TerrariaServer 1.4.5.8 Slime Staff damage is 8. These three generic/summon modifiers round their
        // damage multiplier back to 8, so TryGetPrefixStatMultipliersForItem rejects them and Prefix(-1) rerolls.
        return prefix.Value is not (55 or 89 or 91);
    }

    private static bool Contains(ReadOnlySpan<PrefixId> prefixes, PrefixId prefix)
    {
        for (int index = 0; index < prefixes.Length; index++)
        {
            if (prefixes[index] == prefix)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Source-backed natural Prefix(-1) selection for the currently verified item slice. The random call order mirrors
/// Terraria.Item.Prefix: 1/4 no-prefix check, family selection, reduced-natural-chance check, then validity reroll.
/// </summary>
public static class VanillaNaturalItemPrefixRoller
{
    public static bool CanRoll(ItemTypeId itemType) =>
        VanillaItemDefinitionCatalog.TryGetWorldDrop(itemType, out VanillaItemWorldDropDefinition definition) &&
        definition.PrefixFamily is VanillaItemPrefixFamily.None or VanillaItemPrefixFamily.Summon;

    public static bool TryRoll(
        ItemTypeId itemType,
        INpcLootRollSource random,
        out PrefixId prefix)
    {
        ArgumentNullException.ThrowIfNull(random);
        prefix = default;

        if (!VanillaItemDefinitionCatalog.TryGetWorldDrop(itemType, out VanillaItemWorldDropDefinition definition))
            return false;

        if (definition.PrefixFamily == VanillaItemPrefixFamily.None)
            return true;

        if (definition.PrefixFamily != VanillaItemPrefixFamily.Summon)
            return false;

        ReadOnlySpan<PrefixId> rollable =
            VanillaItemPrefixCatalog.GetRollablePrefixes(VanillaItemPrefixFamily.Summon);
        if (rollable.IsEmpty)
            return false;

        while (true)
        {
            if (random.NextInt32(0, 4) == 0)
            {
                prefix = default;
                return true;
            }

            PrefixId selected = rollable[random.NextInt32(0, rollable.Length)];
            if (VanillaItemPrefixCatalog.HasReducedNaturalChance(selected) &&
                random.NextInt32(0, 3) != 0)
            {
                prefix = default;
                return true;
            }

            if (!VanillaItemPrefixCatalog.IsValidForItem(itemType, selected))
                continue;

            prefix = selected;
            return true;
        }
    }
}
