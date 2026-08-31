using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>Source-verified TerrariaServer 1.4.5.8 rule shapes used by normal-mode King Slime.</summary>
public enum VanillaKingSlimeNormalLootRuleKind : byte
{
    Common = 1,
    OneFromThreeOptions = 2,
    NotScalingWithLuckWithCommonFallback = 3
}

/// <summary>
/// One ordered normal-mode King Slime loot rule. Potential identities are explicit so the world-item transaction
/// can preflight every possible branch before consuming loot RNG.
/// </summary>
public readonly record struct VanillaKingSlimeNormalLootRule(
    VanillaKingSlimeNormalLootRuleKind Kind,
    ItemTypeId PrimaryItem,
    ItemTypeId SecondaryItem,
    ItemTypeId TertiaryItem,
    int ChanceDenominator)
{
    public bool IsValid => Kind switch
    {
        VanillaKingSlimeNormalLootRuleKind.Common =>
            !PrimaryItem.IsNone && SecondaryItem.IsNone && TertiaryItem.IsNone && ChanceDenominator > 0,
        VanillaKingSlimeNormalLootRuleKind.OneFromThreeOptions =>
            !PrimaryItem.IsNone && !SecondaryItem.IsNone && !TertiaryItem.IsNone && ChanceDenominator == 1,
        VanillaKingSlimeNormalLootRuleKind.NotScalingWithLuckWithCommonFallback =>
            !PrimaryItem.IsNone && !SecondaryItem.IsNone && TertiaryItem.IsNone && ChanceDenominator > 0,
        _ => false
    };

    public int PotentialItemCount => Kind switch
    {
        VanillaKingSlimeNormalLootRuleKind.Common => 1,
        VanillaKingSlimeNormalLootRuleKind.OneFromThreeOptions => 3,
        VanillaKingSlimeNormalLootRuleKind.NotScalingWithLuckWithCommonFallback => 2,
        _ => 0
    };

    public ItemTypeId GetPotentialItem(int index) => (Kind, index) switch
    {
        (VanillaKingSlimeNormalLootRuleKind.Common, 0) => PrimaryItem,
        (VanillaKingSlimeNormalLootRuleKind.OneFromThreeOptions, 0) => PrimaryItem,
        (VanillaKingSlimeNormalLootRuleKind.OneFromThreeOptions, 1) => SecondaryItem,
        (VanillaKingSlimeNormalLootRuleKind.OneFromThreeOptions, 2) => TertiaryItem,
        (VanillaKingSlimeNormalLootRuleKind.NotScalingWithLuckWithCommonFallback, 0) => PrimaryItem,
        (VanillaKingSlimeNormalLootRuleKind.NotScalingWithLuckWithCommonFallback, 1) => SecondaryItem,
        _ => default
    };

    public static VanillaKingSlimeNormalLootRule Common(ItemTypeId item, int chanceDenominator) =>
        new(VanillaKingSlimeNormalLootRuleKind.Common, item, default, default, chanceDenominator);

    public static VanillaKingSlimeNormalLootRule OneFromThree(ItemTypeId first, ItemTypeId second, ItemTypeId third) =>
        new(VanillaKingSlimeNormalLootRuleKind.OneFromThreeOptions, first, second, third, 1);

    public static VanillaKingSlimeNormalLootRule NotScalingWithLuckWithFallback(
        ItemTypeId primary,
        int chanceDenominator,
        ItemTypeId fallback) =>
        new(VanillaKingSlimeNormalLootRuleKind.NotScalingWithLuckWithCommonFallback, primary, fallback, default, chanceDenominator);
}

/// <summary>
/// Pinned normal-mode King Slime NPC-specific registration order. The trophy rule is registered before the
/// boss-specific normal rules in Terraria's drop database.
/// </summary>
public static class VanillaKingSlimeNormalLootCatalog
{
    private static readonly VanillaKingSlimeNormalLootRule[] OrderedRules =
    [
        VanillaKingSlimeNormalLootRule.Common(VanillaKingSlimeItemIds.KingSlimeTrophy, 10),
        VanillaKingSlimeNormalLootRule.Common(VanillaKingSlimeItemIds.SlimySaddle, 4),
        VanillaKingSlimeNormalLootRule.Common(VanillaKingSlimeItemIds.KingSlimeMask, 7),
        VanillaKingSlimeNormalLootRule.OneFromThree(
            VanillaKingSlimeItemIds.NinjaHood,
            VanillaKingSlimeItemIds.NinjaShirt,
            VanillaKingSlimeItemIds.NinjaPants),
        VanillaKingSlimeNormalLootRule.NotScalingWithLuckWithFallback(
            VanillaKingSlimeItemIds.SlimeHook,
            3,
            VanillaKingSlimeItemIds.SlimeGun),
        VanillaKingSlimeNormalLootRule.Common(VanillaKingSlimeItemIds.Solidifier, 1),
        VanillaKingSlimeNormalLootRule.Common(VanillaKingSlimeItemIds.SlimeStaff, 30)
    ];

    public const int MaximumDropCount = 7;
    public static ReadOnlySpan<VanillaKingSlimeNormalLootRule> Rules => OrderedRules;

    public static bool IsValid
    {
        get
        {
            if (OrderedRules.Length != MaximumDropCount)
                return false;
            for (int index = 0; index < OrderedRules.Length; index++)
            {
                if (!OrderedRules[index].IsValid)
                    return false;
            }
            return true;
        }
    }
}

/// <summary>Allocation-free evaluator for the source-verified normal-mode King Slime rule shapes.</summary>
public static class VanillaKingSlimeNormalLootEvaluator
{
    public static bool TryEvaluateRule(
        in VanillaKingSlimeNormalLootRule rule,
        INpcLootRollSource rolls,
        out bool dropped,
        out NpcLootDrop drop)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        dropped = false;
        drop = default;
        if (!rule.IsValid)
            return false;

        switch (rule.Kind)
        {
            case VanillaKingSlimeNormalLootRuleKind.Common:
                if (rolls.RollLuck(rule.ChanceDenominator) >= 1)
                    return true;
                rolls.NextInt32(1, 2);
                drop = new NpcLootDrop(rule.PrimaryItem, 1);
                dropped = true;
                return true;

            case VanillaKingSlimeNormalLootRuleKind.OneFromThreeOptions:
                if (rolls.RollLuck(1) >= 1)
                    return true;
                int selected = rolls.NextInt32(0, 3);
                ItemTypeId selectedItem = rule.GetPotentialItem(selected);
                if (selectedItem.IsNone)
                    return false;
                drop = new NpcLootDrop(selectedItem, 1);
                dropped = true;
                return true;

            case VanillaKingSlimeNormalLootRuleKind.NotScalingWithLuckWithCommonFallback:
                if (rolls.NextInt32(0, rule.ChanceDenominator) == 0)
                {
                    rolls.NextInt32(1, 2);
                    drop = new NpcLootDrop(rule.PrimaryItem, 1);
                    dropped = true;
                    return true;
                }
                if (rolls.RollLuck(1) >= 1)
                    return true;
                rolls.NextInt32(1, 2);
                drop = new NpcLootDrop(rule.SecondaryItem, 1);
                dropped = true;
                return true;

            default:
                return false;
        }
    }

    public static bool TryEvaluateAll(
        INpcLootRollSource rolls,
        Span<NpcLootDrop> destination,
        out int dropCount)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        if (!VanillaKingSlimeNormalLootCatalog.IsValid ||
            destination.Length < VanillaKingSlimeNormalLootCatalog.MaximumDropCount)
        {
            dropCount = 0;
            return false;
        }

        dropCount = 0;
        ReadOnlySpan<VanillaKingSlimeNormalLootRule> rules = VanillaKingSlimeNormalLootCatalog.Rules;
        for (int index = 0; index < rules.Length; index++)
        {
            if (!TryEvaluateRule(in rules[index], rolls, out bool dropped, out NpcLootDrop drop))
            {
                dropCount = 0;
                return false;
            }
            if (dropped)
                destination[dropCount++] = drop;
        }
        return true;
    }
}
