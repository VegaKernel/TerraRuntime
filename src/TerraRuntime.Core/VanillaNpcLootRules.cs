using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>Vanilla rule shapes currently source-verified for the initial NPC-loot slice.</summary>
public enum VanillaNpcLootRuleKind : byte
{
    ExtraGel = 1,
    NormalVsExpertCommon = 2
}

/// <summary>
/// Immutable source-backed NPC-specific loot rule. The fields model only rule shapes independently verified from
/// TerrariaServer 1.4.5.8; unsupported rule families are not flattened into a generic probability table.
/// </summary>
public readonly record struct VanillaNpcLootRule(
    VanillaNpcLootRuleKind Kind,
    ItemTypeId ItemType,
    int NormalChanceDenominator,
    int ExpertChanceDenominator,
    short MinimumStack,
    short MaximumStack,
    short ExtraGelMultiplier)
{
    public bool IsValid =>
        (Kind is VanillaNpcLootRuleKind.ExtraGel or VanillaNpcLootRuleKind.NormalVsExpertCommon) &&
        !ItemType.IsNone &&
        NormalChanceDenominator > 0 &&
        ExpertChanceDenominator > 0 &&
        MinimumStack > 0 &&
        MaximumStack >= MinimumStack &&
        ExtraGelMultiplier > 0;

    public static VanillaNpcLootRule ExtraGel(
        ItemTypeId itemType,
        int chanceDenominator,
        short minimumStack,
        short maximumStack,
        short extraGelMultiplier) =>
        new(
            VanillaNpcLootRuleKind.ExtraGel,
            itemType,
            chanceDenominator,
            chanceDenominator,
            minimumStack,
            maximumStack,
            extraGelMultiplier);

    public static VanillaNpcLootRule NormalVsExpertCommon(
        ItemTypeId itemType,
        int normalChanceDenominator,
        int expertChanceDenominator) =>
        new(
            VanillaNpcLootRuleKind.NormalVsExpertCommon,
            itemType,
            normalChanceDenominator,
            expertChanceDenominator,
            MinimumStack: 1,
            MaximumStack: 1,
            ExtraGelMultiplier: 1);
}

/// <summary>Source-backed NPC-specific vanilla loot rules implemented by TerraRuntime.</summary>
public static class VanillaNpcLootRuleCatalog
{
    private static readonly VanillaNpcLootRule[] BlueSlimeRules =
    [
        VanillaNpcLootRule.ExtraGel(
            VanillaItemIds.Gel,
            chanceDenominator: 1,
            minimumStack: 1,
            maximumStack: 2,
            extraGelMultiplier: 2),
        VanillaNpcLootRule.NormalVsExpertCommon(
            VanillaItemIds.SlimeStaff,
            normalChanceDenominator: 10_000,
            expertChanceDenominator: 7_000)
    ];

    /// <summary>
    /// Returns the exact NPC-specific rule sequence currently imported for <paramref name="npcType"/>. Global,
    /// world-condition and other unimported rule layers are intentionally outside this initial catalog.
    /// </summary>
    public static ReadOnlySpan<VanillaNpcLootRule> GetNpcSpecificRules(NpcTypeId npcType)
    {
        if (npcType == VanillaNpcIds.BlueSlime)
            return BlueSlimeRules;

        return ReadOnlySpan<VanillaNpcLootRule>.Empty;
    }
}

/// <summary>
/// Runtime-owned execution context corresponding to the semantic condition inputs consumed by the currently
/// implemented vanilla wrappers. Master mode is represented by <see cref="IsExpertMode"/> because the source
/// wrapper branches on DropAttemptInfo.IsExpertMode.
/// </summary>
public readonly record struct VanillaNpcLootContext(
    bool IsExpertMode,
    bool DropExtraGel);

/// <summary>
/// Semantic random/luck boundary matching Terraria's CommonDrop call order. Implementations own player-luck
/// semantics; the loot engine deliberately does not silently substitute process Random for Player.RollLuck.
/// </summary>
public interface INpcLootRollSource
{
    int RollLuck(int chanceDenominator);

    int NextInt32(int inclusiveMin, int exclusiveMax);
}

/// <summary>One concrete item drop produced by evaluated NPC loot rules.</summary>
public readonly record struct NpcLootDrop(ItemTypeId ItemType, short Stack)
{
    public bool IsValid => !ItemType.IsNone && Stack > 0;
}

/// <summary>
/// Allocation-free evaluator for the source-backed NPC-specific rule slice. Rule order, luck roll order and the
/// inclusive CommonDrop stack range follow the pinned TerrariaServer 1.4.5.8 source contract.
/// </summary>
public static class VanillaNpcLootEvaluator
{
    public static bool TryEvaluateNpcSpecificRules(
        NpcTypeId npcType,
        in VanillaNpcLootContext context,
        INpcLootRollSource rolls,
        Span<NpcLootDrop> destination,
        out int dropCount)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ReadOnlySpan<VanillaNpcLootRule> rules = VanillaNpcLootRuleCatalog.GetNpcSpecificRules(npcType);
        if (rules.IsEmpty || destination.Length < rules.Length)
        {
            dropCount = 0;
            return false;
        }

        dropCount = 0;
        for (int index = 0; index < rules.Length; index++)
        {
            VanillaNpcLootRule rule = rules[index];
            if (!rule.IsValid)
            {
                dropCount = 0;
                return false;
            }

            int denominator = context.IsExpertMode
                ? rule.ExpertChanceDenominator
                : rule.NormalChanceDenominator;

            // CommonDrop always performs Player.RollLuck before any stack RNG, including denominator 1.
            if (rolls.RollLuck(denominator) >= 1)
                continue;

            int multiplier = rule.Kind == VanillaNpcLootRuleKind.ExtraGel && context.DropExtraGel
                ? rule.ExtraGelMultiplier
                : 1;
            int inclusiveMin = checked(rule.MinimumStack * multiplier);
            int inclusiveMax = checked(rule.MaximumStack * multiplier);
            int stack = rolls.NextInt32(inclusiveMin, checked(inclusiveMax + 1));
            destination[dropCount++] = new NpcLootDrop(rule.ItemType, checked((short)stack));
        }

        return true;
    }
}
