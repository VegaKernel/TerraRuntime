using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Immutable, bounded source alternatives; no arbitrary recursive rule graph is admitted.</summary>
public sealed class VanillaNpcLootAlternatives1458
{
    private readonly VanillaNpcLootRule[] rules;
    public VanillaNpcLootAlternatives1458(ReadOnlySpan<VanillaNpcLootRule> alternatives)
    {
        if (alternatives.Length is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(alternatives));
        rules = alternatives.ToArray();
        foreach (ref readonly var rule in Rules)
            if (!rule.IsValid || rule.Kind == VanillaNpcLootRuleKind.FailedRollChain)
                throw new ArgumentException("Only verified nonrecursive alternatives are admitted.", nameof(alternatives));
    }
    public ReadOnlySpan<VanillaNpcLootRule> Rules => rules;
    public bool IsValid => rules.Length is >= 2 and <= 4;
}

/// <summary>Immutable source selection order for the admitted two/three-item option rules.</summary>
public sealed class VanillaNpcLootOptions1458
{
    private readonly ItemTypeId[] items;
    public VanillaNpcLootOptions1458(ReadOnlySpan<ItemTypeId> options)
    {
        if (options.Length is < 2 or > 3) throw new ArgumentOutOfRangeException(nameof(options));
        items = options.ToArray();
        foreach (var item in items)
            if (item.IsNone) throw new ArgumentException("Options must have verified identities.", nameof(options));
    }
    public ReadOnlySpan<ItemTypeId> Items => items;
    public bool IsValid => items.Length is >= 2 and <= 3;
}

/// <summary>1.4.5.8 ItemDropDatabase undead registrations; table support does not grant actor/AI support.</summary>
public static class VanillaUndeadLootCatalog1458
{
    private static readonly Dictionary<int, VanillaNpcLootTable> Tables = CreateTables();

    public static bool TryGet(NpcTypeId type, out VanillaNpcLootTable table) => Tables.TryGetValue(type.Value, out table);

    public static bool HasSickleCondition(NpcTypeId type) => type.Value is
        3 or 591 or 590 or 331 or 332 or 132 or 161 or 186 or 187 or 188 or 189 or 200 or 223 or
        319 or 320 or 321 or 430 or 431 or 432 or 433 or 434 or 435 or 436;

    private static VanillaNpcLootRule Common(ItemTypeId item, int chance = 1, short min = 1, short max = 1,
        VanillaNpcLootRuleKind kind = VanillaNpcLootRuleKind.NormalVsExpertCommon) =>
        new(kind, item, chance, chance, min, max, 1);

    private static VanillaNpcLootRule Chain(params VanillaNpcLootRule[] rules) =>
        new(VanillaNpcLootRuleKind.FailedRollChain, rules[0].ItemType, 1, 1, 1, 1, 1,
            new(rules));

    private static VanillaNpcLootRule Options(int chance, params ItemTypeId[] items) =>
        new(VanillaNpcLootRuleKind.OneFromOptions, items[0], chance, chance, 1, 1, 1, Options: new(items));

    private static Dictionary<int, VanillaNpcLootTable> CreateTables()
    {
        var result = new Dictionary<int, VanillaNpcLootTable>();
        void Add(int id, IEnumerable<VanillaNpcLootRule> rules) => result.Add(id, new(new(id), rules.ToArray()));
        // Food is registered before RegisterMiscDrops. NPC635 intentionally has no Hook rule.
        foreach (int id in new[] { 21, 201, 202, 203, 322, 323, 324, 635, 449, 450, 451, 452 })
        {
            var rules = new List<VanillaNpcLootRule>
            {
                Common(VanillaUndeadDropItemIds1458.MilkCarton, 150, kind: VanillaNpcLootRuleKind.NotFromStatueCommon),
                Chain(Common(VanillaUndeadDropItemIds1458.AncientIronHelmet, 100), Common(VanillaUndeadDropItemIds1458.AncientGoldHelmet, 200), Common(VanillaUndeadDropItemIds1458.BoneSword, 200), Common(VanillaUndeadDropItemIds1458.Skull, 500))
            };
            if (id != 635) rules.Add(Common(VanillaUndeadDropItemIds1458.Hook, 25));
            Add(id, rules);
        }
        foreach (int id in new[] { 31, 32, 34, 294, 295, 296, 693 })
        {
            var rules = new List<VanillaNpcLootRule>();
            if (id == 34) rules.Add(Common(VanillaUndeadDropItemIds1458.CreamSoda, 70, kind: VanillaNpcLootRuleKind.NotFromStatueCommon));
            else
            {
                rules.Add(Common(VanillaUndeadDropItemIds1458.AncientNecroHelmet, 450));
                rules.Add(Common(VanillaUndeadDropItemIds1458.ClothierVoodooDoll, 300));
                if (id == 32) rules.Add(Common(VanillaUndeadDropItemIds1458.PaintingDarkForebodings, 150));
            }
            rules.Add(Chain(Common(VanillaUndeadDropItemIds1458.BoneWand, 250), Common(VanillaUndeadDropItemIds1458.TallyCounter, 100), Common(VanillaItemIds.GoldenKey, 65),
                Common(VanillaItemIds.Bone, 1, 1, 3, VanillaNpcLootRuleKind.NotExpertCommon)));
            rules.Add(Common(VanillaItemIds.Bone, 1, 2, 6, VanillaNpcLootRuleKind.ExpertCommon));
            // RegisterStatusImmunityItems runs after miscellaneous registrations.
            if (id == 34) rules.Add(Common(VanillaUndeadDropItemIds1458.Nazar, 100, kind: VanillaNpcLootRuleKind.ExpertGetsOneReroll));
            Add(id, rules);
        }
        Add(110, [Common(VanillaUndeadDropItemIds1458.Marrow, 200), Common(VanillaItemIds.MagicQuiver, 40)]);

        foreach (int id in new[] { 3, 591, 590, 331, 332, 132, 161, 186, 187, 188, 189, 200, 223,
                     319, 320, 321, 430, 431, 432, 433, 434, 435, 436 })
        {
            var rules = new List<VanillaNpcLootRule>();
            if (id is 161 or 431) rules.Add(Options(10, VanillaUndeadDropItemIds1458.EskimoHood, VanillaUndeadDropItemIds1458.EskimoCoat, VanillaUndeadDropItemIds1458.EskimoPants));
            if (id is 186 or 432) rules.Add(Common(VanillaItemIds.WoodenArrow, 1, 1, 9));
            if (id is 187 or 433)
            {
                rules.Add(VanillaNpcLootRule.ExtraGel(VanillaItemIds.Gel, 1, 1, 2, 2));
                rules.Add(VanillaNpcLootRule.NormalVsExpertCommon(VanillaItemIds.SlimeStaff, 10000, 7000));
            }
            rules.Add(Common(VanillaNpcSpecificDropItemIds.Shackle, 50));
            rules.Add(Common(VanillaNpcSpecificDropItemIds.ZombieArm, 250));
            rules.Add(Common(VanillaNpcSpecificDropItemIds.SpiffoPlush, 1500));
            rules.Add(Common(VanillaNpcSpecificDropItemIds.Sickle, 15, kind: VanillaNpcLootRuleKind.SkyblockSickleCommon));
            if (id is 590 or 591) rules.Add(Common(VanillaItemIds.Torch, 1, 5, 20));
            if (id is 188 or 189 or 434 or 435)
                rules.Add(Common(VanillaUndeadDropItemIds1458.Wood, 2, 5, 20, VanillaNpcLootRuleKind.LowTilesCommon));
            if (id == 223)
            {
                rules.Add(Options(20, VanillaUndeadDropItemIds1458.RainHat, VanillaUndeadDropItemIds1458.RainCoat));
                rules.Add(Common(VanillaUndeadDropItemIds1458.Glowstick, 1, 1, 4));
            }
            Add(id, rules);
        }
        return result;
    }
}
