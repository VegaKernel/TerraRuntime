using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Source-backed vanilla prefix families currently needed by authoritative gameplay.</summary>
public enum VanillaItemPrefixFamily : byte
{
    None = 0,
    Summon = 1,
    Sword = 2,
    Ranged = 3,
    Magic = 4,
    Spear = 5,
    Accessory = 6
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
    // PrefixLegacy.Prefixes, in source selection order. Families do not by themselves admit item use.
    private static readonly PrefixId[] SwordPrefixes = Prefixes(
        [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,36,37,38,53,54,55,39,40,56,41,57,42,43,44,45,46,47,48,49,50,51,59,60,61,81]);
    private static readonly PrefixId[] RangedPrefixes = Prefixes(
        [16,17,18,19,20,21,22,23,24,25,58,36,37,38,53,54,55,39,40,56,41,57,42,44,45,46,47,48,49,50,51,59,60,61,82]);
    private static readonly PrefixId[] MagicPrefixes = Prefixes(
        [26,27,28,29,30,31,32,33,34,35,52,36,37,38,53,54,55,39,40,56,41,57,42,43,44,45,46,47,48,49,50,51,59,60,61,83]);
    private static readonly PrefixId[] SpearPrefixes = Prefixes(
        [36,37,38,53,54,55,39,40,56,41,57,59,60,61]);
    private static readonly PrefixId[] AccessoryPrefixes = Prefixes(
        [62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80]);

    private static PrefixId[] Prefixes(ReadOnlySpan<byte> values)
    {
        var result = new PrefixId[values.Length];
        for (int index = 0; index < values.Length; index++)
            result[index] = new PrefixId(values[index]);
        return result;
    }

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

    public static ReadOnlySpan<PrefixId> GetRollablePrefixes(VanillaItemPrefixFamily family) => family switch
    {
        VanillaItemPrefixFamily.Summon => SummonPrefixes,
        VanillaItemPrefixFamily.Sword => SwordPrefixes,
        VanillaItemPrefixFamily.Ranged => RangedPrefixes,
        VanillaItemPrefixFamily.Magic => MagicPrefixes,
        VanillaItemPrefixFamily.Spear => SpearPrefixes,
        VanillaItemPrefixFamily.Accessory => AccessoryPrefixes,
        _ => ReadOnlySpan<PrefixId>.Empty
    };

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
    /// exact knowledge for Slime Staff, Blade Staff, Dungeon chests and Plantera/Golem death-loot items.
    /// Prefix zero is a valid natural-roll result.
    /// </summary>
    public static bool IsValidForItem(ItemTypeId itemType, PrefixId prefix)
    {
        if (VanillaDungeonChestItemCatalog1458.TryGet(itemType, out VanillaItemDefinition dungeon) &&
            dungeon.WorldDrop is { PrefixFamily: not VanillaItemPrefixFamily.None } dungeonDrop)
        {
            // Independent Item.TryGetPrefixStatMultipliersForItem probe accepts every family member
            // for these exact defaults; no rounding exclusion or guessed generic-item validity.
            return prefix == VanillaPrefixIds.None || Contains(GetRollablePrefixes(dungeonDrop.PrefixFamily), prefix);
        }
        if (VanillaGolemItemCatalog1458.TryGet(itemType, out VanillaItemDefinition golem) &&
            golem.WorldDrop is { PrefixFamily: not VanillaItemPrefixFamily.None } golemDrop)
        {
            // Official 1.4.5.8 stat-guard probe: only Heat Ray rejects Nimble (10 * .95 rounds to 10).
            return prefix == VanillaPrefixIds.None ||
                (Contains(GetRollablePrefixes(golemDrop.PrefixFamily), prefix) &&
                 (itemType != VanillaGolemItemIds.HeatRay || prefix != VanillaPrefixIds.Nimble));
        }
        if (VanillaPlanteraItemCatalog1458.TryGet(itemType, out VanillaItemDefinition plantera) &&
            plantera.WorldDrop is { PrefixFamily: not VanillaItemPrefixFamily.None } worldDrop)
        {
            // Item.SetDefaults + TryGetPrefixStatMultipliersForItem, independently checked against
            // official 1.4.5.8 for every family member. Only Venus Magnum (useAnimation=9) rejects
            // Deadly / Nimble: their 0.95 speed multiplier rounds the animation back to 9.
            return prefix == VanillaPrefixIds.None ||
                (Contains(GetRollablePrefixes(worldDrop.PrefixFamily), prefix) &&
                 (itemType != VanillaPlanteraItemIds.VenusMagnum ||
                  (prefix != VanillaPrefixIds.Deadly && prefix != VanillaPrefixIds.Nimble)));
        }
        // Blade Staff: damage 6, knockBack 0. Item.TryGetPrefixStatMultipliersForItem rejects
        // unchanged rounded damage and every non-unit knockback modifier. PrefixLegacy's Summon
        // family therefore leaves these seven prefixes (plus the no-prefix result), not Slime Staff's set.
        if (itemType == VanillaQueenSlimeItemIds.BladeStaff)
            return prefix == VanillaPrefixIds.None ||
                   prefix == VanillaPrefixIds.Worthy ||
                   prefix == VanillaPrefixIds.Focused ||
                   prefix == VanillaPrefixIds.Petty ||
                   prefix == VanillaPrefixIds.Eager ||
                   prefix == VanillaPrefixIds.Ballistic ||
                   prefix == VanillaPrefixIds.Hurtful ||
                   prefix == VanillaPrefixIds.Damaged;

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
        VanillaDefinitionCatalog.TryGetWorldDrop(itemType, out VanillaItemWorldDropDefinition definition) &&
        (definition.PrefixFamily == VanillaItemPrefixFamily.None ||
         (!VanillaItemPrefixCatalog.GetRollablePrefixes(definition.PrefixFamily).IsEmpty &&
          VanillaItemPrefixCatalog.IsValidForItem(itemType, VanillaPrefixIds.None)));

    public static bool TryRoll(
        ItemTypeId itemType,
        INpcLootRollSource random,
        out PrefixId prefix)
    {
        ArgumentNullException.ThrowIfNull(random);
        prefix = default;

        if (!VanillaDefinitionCatalog.TryGetWorldDrop(itemType, out VanillaItemWorldDropDefinition definition))
            return false;

        if (definition.PrefixFamily == VanillaItemPrefixFamily.None)
            return true;

        if (!CanRoll(itemType))
            return false;

        ReadOnlySpan<PrefixId> rollable =
            VanillaItemPrefixCatalog.GetRollablePrefixes(definition.PrefixFamily);
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
