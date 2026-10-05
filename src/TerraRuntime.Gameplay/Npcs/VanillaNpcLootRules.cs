using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Vanilla rule shapes currently source-verified for the initial NPC-loot slice.</summary>
public enum VanillaNpcLootRuleKind : byte
{
    ExtraGel = 1,
    NormalVsExpertCommon = 2,
    NotFromStatueCommon = 3,
    SkyblockSickleCommon = 4
}

/// <summary>
/// Immutable source-backed NPC-specific loot rule. The fields model only rule shapes independently verified from
/// TerrariaServer 1.4.5.8; unsupported rule families are not flattened into a generic probability table.
/// Every currently admitted rule emits at most one item stack when it succeeds.
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
        (Kind is VanillaNpcLootRuleKind.ExtraGel or VanillaNpcLootRuleKind.NormalVsExpertCommon or
            VanillaNpcLootRuleKind.NotFromStatueCommon or VanillaNpcLootRuleKind.SkyblockSickleCommon) &&
        !ItemType.IsNone &&
        NormalChanceDenominator > 0 &&
        ExpertChanceDenominator > 0 &&
        MinimumStack > 0 &&
        MaximumStack >= MinimumStack &&
        ExtraGelMultiplier > 0;

    public int MaximumDropCount => IsValid ? 1 : 0;

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

/// <summary>
/// Immutable runtime view of one source-backed NPC-specific loot table. The backing rule array is private and
/// exposed only as <see cref="ReadOnlySpan{T}"/>, so catalog registration order cannot be mutated by gameplay callers.
/// A table is a support boundary: catalog lookup failure means unsupported, while a future verified table may
/// legitimately contain zero NPC-specific rules without being confused with an unknown NPC.
/// </summary>
public readonly struct VanillaNpcLootTable
{
    private readonly VanillaNpcLootRule[]? _rules;

    internal VanillaNpcLootTable(NpcTypeId npcType, VanillaNpcLootRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        NpcType = npcType;
        _rules = rules;
    }

    public NpcTypeId NpcType { get; }

    public ReadOnlySpan<VanillaNpcLootRule> Rules =>
        _rules is null ? ReadOnlySpan<VanillaNpcLootRule>.Empty : _rules;

    public int RuleCount => _rules?.Length ?? 0;

    public int MaximumDropCount
    {
        get
        {
            int count = 0;
            ReadOnlySpan<VanillaNpcLootRule> rules = Rules;
            for (int index = 0; index < rules.Length; index++)
                count = checked(count + rules[index].MaximumDropCount);
            return count;
        }
    }

    public bool IsValid
    {
        get
        {
            if (!NpcType.IsAssigned || _rules is null)
                return false;

            for (int index = 0; index < _rules.Length; index++)
            {
                if (!_rules[index].IsValid)
                    return false;
            }

            return true;
        }
    }
}

/// <summary>Source-backed NPC-specific vanilla loot tables implemented by TerraRuntime.</summary>
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

    private static readonly VanillaNpcLootTable BlueSlimeTable = new(
        VanillaNpcIds.BlueSlime,
        BlueSlimeRules);

    // RegisterFoodDrops runs before the ordinary slime registration in ItemDropDatabase.Populate.
    private static readonly VanillaNpcLootRule[] IceSlimeRules =
    [
        new(VanillaNpcLootRuleKind.NotFromStatueCommon, VanillaItemIds.IceCream, 150, 150, 1, 1, 1),
        BlueSlimeRules[0],
        BlueSlimeRules[1]
    ];
    private static readonly VanillaNpcLootRule[] SandSlimeRules =
    [
        VanillaNpcLootRule.ExtraGel(VanillaItemIds.Gel, 1, 2, 3, 2),
        VanillaNpcLootRule.NormalVsExpertCommon(VanillaItemIds.SlimeStaff, 8000, 5600)
    ];
    private static readonly VanillaNpcLootTable IceSlimeTable = new(VanillaNpcIds.IceSlime, IceSlimeRules);
    private static readonly VanillaNpcLootTable SpikedIceSlimeTable = new(VanillaNpcIds.SpikedIceSlime, IceSlimeRules);
    private static readonly VanillaNpcLootTable SandSlimeTable = new(VanillaNpcIds.SandSlime, SandSlimeRules);
    private static readonly VanillaNpcLootTable SpikedSlimeTable = new(VanillaNpcIds.SpikedSlime, BlueSlimeRules);
    private static readonly VanillaNpcLootTable SlimerTable = new(VanillaNpcIds.Slimer, []);
    private static readonly VanillaNpcLootTable ZombieTable = new(VanillaNpcIds.Zombie,
    [
        VanillaNpcLootRule.NormalVsExpertCommon(VanillaNpcSpecificDropItemIds.Shackle, 50, 50),
        VanillaNpcLootRule.NormalVsExpertCommon(VanillaNpcSpecificDropItemIds.ZombieArm, 250, 250),
        VanillaNpcLootRule.NormalVsExpertCommon(VanillaNpcSpecificDropItemIds.SpiffoPlush, 1500, 1500),
        new(VanillaNpcLootRuleKind.SkyblockSickleCommon, VanillaNpcSpecificDropItemIds.Sickle, 15, 15, 1, 1, 1)
    ]);
    private static readonly VanillaNpcLootTable MeteorHeadTable = new(VanillaNpcIds.MeteorHead,
    [
        VanillaNpcLootRule.NormalVsExpertCommon(VanillaNpcSpecificDropItemIds.Meteorite, 50, 50),
        VanillaNpcLootRule.NormalVsExpertCommon(VanillaNpcSpecificDropItemIds.TheSeaOfSilence, 100, 100)
    ]);

    /// <summary>
    /// Resolves an explicitly imported NPC-specific loot table. Lookup failure is the unsupported signal; callers
    /// should not use an empty span as a proxy for support because a verified NPC may legitimately have no rules.
    /// </summary>
    public static bool TryGetNpcSpecificTable(NpcTypeId npcType, out VanillaNpcLootTable table)
    {
        if (npcType == VanillaNpcIds.BlueSlime)
        {
            table = BlueSlimeTable;
            return table.IsValid;
        }
        if (npcType == VanillaNpcIds.IceSlime) { table = IceSlimeTable; return true; }
        if (npcType == VanillaNpcIds.SpikedIceSlime) { table = SpikedIceSlimeTable; return true; }
        if (npcType == VanillaNpcIds.SandSlime) { table = SandSlimeTable; return true; }
        if (npcType == VanillaNpcIds.SpikedSlime) { table = SpikedSlimeTable; return true; }
        if (npcType == VanillaNpcIds.Slimer) { table = SlimerTable; return true; }
        if (npcType == VanillaNpcIds.Zombie) { table = ZombieTable; return true; }
        if (npcType == VanillaNpcIds.MeteorHead) { table = MeteorHeadTable; return true; }

        table = default;
        return false;
    }
}

/// <summary>
/// Runtime-owned execution context corresponding to the semantic condition inputs consumed by the currently
/// implemented vanilla wrappers. Master mode is represented by <see cref="IsExpertMode"/> because the source
/// wrapper branches on DropAttemptInfo.IsExpertMode.
/// </summary>
public readonly record struct VanillaNpcLootContext(
    bool IsExpertMode,
    bool DropExtraGel,
    bool SpawnedFromStatue = false,
    bool? LowTiles = null,
    bool? HasSickle = null);

/// <summary>
/// Semantic random/luck boundary matching Terraria's CommonDrop call order. For production NPC loot the source
/// proves the random side is Main.rand, the same stream later consumed by Item.NewItem prefix/velocity behavior.
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
/// Allocation-free evaluator for source-backed NPC-specific loot tables. Rule order, luck roll order and the
/// inclusive CommonDrop stack range follow the pinned TerrariaServer 1.4.5.8 source contract.
/// </summary>
public static class VanillaNpcLootEvaluator
{
    /// <summary>Checks the Sickle condition's exact owned facts before any table luck or stack RNG.</summary>
    public static bool TryValidateNpcSpecificContext(in VanillaNpcLootTable table, in VanillaNpcLootContext context)
    {
        if (!table.IsValid) return false;
        foreach (ref readonly var rule in table.Rules)
            if (rule.Kind == VanillaNpcLootRuleKind.SkyblockSickleCommon &&
                !TryGetSickleEligibility(in context, out _)) return false;
        return true;
    }

    private static bool TryGetSickleEligibility(in VanillaNpcLootContext context, out bool eligible)
    {
        eligible = false;
        // HasSickle means Player.HasItemInInventoryOrOpenVoidBag(1786), not an inventory-only approximation.
        if (context.LowTiles == false || context.HasSickle == true) return true;
        if (!context.LowTiles.HasValue || !context.HasSickle.HasValue) return false;
        eligible = true;
        return true;
    }

    /// <summary>
    /// Evaluates one verified rule without buffering later rules. This boundary exists because vanilla CommonDrop
    /// immediately materializes a successful item before the next rule executes, and both paths consume Main.rand.
    /// </summary>
    public static bool TryEvaluateRule(
        in VanillaNpcLootRule rule,
        in VanillaNpcLootContext context,
        INpcLootRollSource rolls,
        out bool dropped,
        out NpcLootDrop drop)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        dropped = false;
        drop = default;

        if (!rule.IsValid)
            return false;
        if (rule.Kind == VanillaNpcLootRuleKind.NotFromStatueCommon && context.SpawnedFromStatue)
            return true;
        if (rule.Kind == VanillaNpcLootRuleKind.SkyblockSickleCommon)
        {
            if (!TryGetSickleEligibility(in context, out bool eligible)) return false;
            if (!eligible) return true;
        }

        int denominator = context.IsExpertMode
            ? rule.ExpertChanceDenominator
            : rule.NormalChanceDenominator;

        // CommonDrop always performs Player.RollLuck before any stack RNG, including denominator 1.
        if (rolls.RollLuck(denominator) >= 1)
            return true;

        int multiplier = rule.Kind == VanillaNpcLootRuleKind.ExtraGel && context.DropExtraGel
            ? rule.ExtraGelMultiplier
            : 1;
        int inclusiveMin = checked(rule.MinimumStack * multiplier);
        int inclusiveMax = checked(rule.MaximumStack * multiplier);
        int stack = rolls.NextInt32(inclusiveMin, checked(inclusiveMax + 1));
        drop = new NpcLootDrop(rule.ItemType, checked((short)stack));
        dropped = true;
        return true;
    }

    public static bool TryEvaluateNpcSpecificTable(
        in VanillaNpcLootTable table,
        in VanillaNpcLootContext context,
        INpcLootRollSource rolls,
        Span<NpcLootDrop> destination,
        out int dropCount)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        if (!TryValidateNpcSpecificContext(in table, in context) || destination.Length < table.MaximumDropCount)
        {
            dropCount = 0;
            return false;
        }

        ReadOnlySpan<VanillaNpcLootRule> rules = table.Rules;
        dropCount = 0;
        for (int index = 0; index < rules.Length; index++)
        {
            if (!TryEvaluateRule(
                    in rules[index],
                    in context,
                    rolls,
                    out bool dropped,
                    out NpcLootDrop drop))
            {
                dropCount = 0;
                return false;
            }

            if (dropped)
                destination[dropCount++] = drop;
        }

        return true;
    }

    public static bool TryEvaluateNpcSpecificRules(
        NpcTypeId npcType,
        in VanillaNpcLootContext context,
        INpcLootRollSource rolls,
        Span<NpcLootDrop> destination,
        out int dropCount)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        if (!VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(npcType, out VanillaNpcLootTable table))
        {
            dropCount = 0;
            return false;
        }

        return TryEvaluateNpcSpecificTable(in table, in context, rolls, destination, out dropCount);
    }
}
