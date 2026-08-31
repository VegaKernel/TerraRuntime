using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>Source-backed vanilla prefix families currently needed by authoritative gameplay.</summary>
public enum VanillaItemPrefixFamily : byte
{
    None = 0,
    Summon = 1
}

public readonly record struct VanillaPrefixDefinition(
    PrefixId Type,
    bool IsSummonRollable,
    bool HasReducedNaturalChance)
{
    public bool IsPresent => Type != VanillaPrefixIds.None;
}

/// <summary>
/// Sparse TerrariaServer 1.4.5.8 prefix catalog. Numeric IDs are version data and are kept here rather than
/// leaking through loot/world-item orchestration. Absence means unverified, not impossible in vanilla.
/// </summary>
public static class VanillaItemPrefixCatalog
{
    private static readonly PrefixId[] SummonPrefixes =
    [
        VanillaPrefixIds.Fabled,
        VanillaPrefixIds.Loyal,
        VanillaPrefixIds.Worthy,
        VanillaPrefixIds.Focused,
        VanillaPrefixIds.Patient,
        VanillaPrefixIds.Rabid,
        VanillaPrefixIds.IllTempered,
        VanillaPrefixIds.Petty,
        VanillaPrefixIds.Feeble,
        VanillaPrefixIds.Skittish,
        VanillaPrefixIds.Eager,
        VanillaPrefixIds.Ballistic,
        VanillaPrefixIds.Scraggling,
        VanillaPrefixIds.Unpleasant,
        VanillaPrefixIds.Forceful,
        VanillaPrefixIds.Strong,
        VanillaPrefixIds.Hurtful,
        VanillaPrefixIds.Ruthless,
        VanillaPrefixIds.Damaged,
        VanillaPrefixIds.Weak,
        VanillaPrefixIds.Shoddy,
        VanillaPrefixIds.Broken
    ];

    public const int Count = VanillaPrefixIds.Count;

    public static bool TryGetDefinition(PrefixId type, out VanillaPrefixDefinition definition)
    {
        if (!VanillaPrefixIds.TryCreate(type.Value, out _))
        {
            definition = default;
            return false;
        }

        definition = new VanillaPrefixDefinition(
            type,
            Contains(SummonPrefixes, type),
            HasReducedNaturalChance(type));
        return true;
    }

    public static ReadOnlySpan<PrefixId> GetRollablePrefixes(VanillaItemPrefixFamily family) =>
        family == VanillaItemPrefixFamily.Summon
            ? SummonPrefixes
            : ReadOnlySpan<PrefixId>.Empty;

    public static bool HasReducedNaturalChance(PrefixId prefix) =>
        prefix == VanillaPrefixIds.Tiny ||
        prefix == VanillaPrefixIds.Terrible ||
        prefix == VanillaPrefixIds.Small ||
        prefix == VanillaPrefixIds.Dull ||
        prefix == VanillaPrefixIds.Unhappy ||
        prefix == VanillaPrefixIds.Awful ||
        prefix == VanillaPrefixIds.Lethargic ||
        prefix == VanillaPrefixIds.Awkward ||
        prefix == VanillaPrefixIds.Inept ||
        prefix == VanillaPrefixIds.Ignorant ||
        prefix == VanillaPrefixIds.Deranged ||
        prefix == VanillaPrefixIds.Broken ||
        prefix == VanillaPrefixIds.Damaged ||
        prefix == VanillaPrefixIds.Shoddy ||
        prefix == VanillaPrefixIds.Slow ||
        prefix == VanillaPrefixIds.Sluggish ||
        prefix == VanillaPrefixIds.Lazy ||
        prefix == VanillaPrefixIds.Weak;

    /// <summary>
    /// Item-specific prefix validity after Terraria's stat-rounding guards. The current catalog only claims
    /// exact knowledge for Slime Staff. Prefix zero is the valid no-prefix result of natural rolling.
    /// </summary>
    public static bool IsValidForItem(ItemTypeId itemType, PrefixId prefix)
    {
        if (itemType != VanillaItemIds.SlimeStaff)
            return false;

        if (prefix == VanillaPrefixIds.None)
            return true;

        if (!Contains(GetRollablePrefixes(VanillaItemPrefixFamily.Summon), prefix))
            return false;

        // TerrariaServer 1.4.5.8 Slime Staff damage is 8. These three generic/summon modifiers round their
        // damage multiplier back to 8, so TryGetPrefixStatMultipliersForItem rejects them and Prefix(-1) rerolls.
        return prefix != VanillaPrefixIds.Unpleasant &&
               prefix != VanillaPrefixIds.Patient &&
               prefix != VanillaPrefixIds.IllTempered;
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
