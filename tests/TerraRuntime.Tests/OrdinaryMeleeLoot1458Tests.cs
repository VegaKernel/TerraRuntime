using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class OrdinaryMeleeLoot1458Tests
{
    private static IEnumerable<object[]> Rows(string collection, string resource = "OrdinaryMeleeLootLinux1458")
    {
        using var stream = typeof(OrdinaryMeleeLoot1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty(collection).EnumerateArray()) yield return [row.GetRawText()];
    }
    public static IEnumerable<object[]> Graphs() => Rows("graphs");
    public static IEnumerable<object[]> Defaults() => Rows("defaults");
    public static IEnumerable<object[]> Prefixes() => Rows("prefixCases");
    public static IEnumerable<object[]> SpecificDrops() => Rows("cases").Concat(Rows("cases", "OrdinaryMeleeLootRareLinux1458"));

    [Theory, MemberData(nameof(Graphs))]
    public void Entire_specific_rule_graph_preserves_original_registration_order_and_shapes(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var type = new NpcTypeId(row.GetProperty("type").GetInt32());
        Assert.True(VanillaOrdinaryMeleeLootCatalog1458.TryGet(type, out var table));
        Assert.True(table.IsValid);
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(type, out var registered));
        Assert.Equal(table.RuleCount, registered.RuleCount);
        var originals = row.GetProperty("rules"); Assert.Equal(originals.GetArrayLength(), table.RuleCount);
        for (int i = 0; i < table.RuleCount; i++) AssertRule(originals[i], table.Rules[i]);
    }

    [Theory, MemberData(nameof(Defaults))]
    public void All_reward_defaults_and_prefix_families_match_original_without_item_use_admission(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var type = new ItemTypeId(row.GetProperty("id").GetInt32());
        Assert.True(VanillaOrdinaryMeleeDropCatalog1458.TryGet(type, out var sparse));
        Assert.Equal((row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32(), row.GetProperty("maxStack").GetInt32()),
            (sparse.RuntimeDefaults.Width, sparse.RuntimeDefaults.Height, (int)sparse.RuntimeDefaults.MaximumStack));
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), sparse.WorldDrop!.Value.NoGravity);
        Assert.Null(sparse.UseTiming); Assert.Null(sparse.Placement); Assert.Null(sparse.PickTool);
        var source = row.GetProperty("rollable");
        int[] prefixes = source.ValueKind == JsonValueKind.Null ? [] : source.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        Assert.Equal(prefixes, VanillaItemPrefixCatalog.GetRollablePrefixes(VanillaOrdinaryMeleeDropCatalog1458.GetPrefixFamily(type))
            .ToArray().Select(p => (int)p.Value).ToArray());
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(type));
    }

    [Theory, MemberData(nameof(Prefixes))]
    public void Every_reward_natural_prefix_preserves_original_full_random_state(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var type = new ItemTypeId(row.GetProperty("id").GetInt32());
        AssertRandom(row.GetProperty("before"), random);
        Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(type, new Rolls(random, 0f), out var prefix));
        Assert.Equal(row.GetProperty("prefix").GetInt32(), prefix.Value);
        AssertRandom(row.GetProperty("after"), random);
    }

    // Actual original GetRulesForNPCID(false), ResolveRule and immediate Item.NewItem;
    // compare each top-level boundary, including no-drop conditions and stack/prefix/velocity draws.
    [Theory, MemberData(nameof(SpecificDrops))]
    public void Specific_loot_and_interleaved_materialization_match_original_full_random_checkpoints(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(row.GetProperty("type").GetInt32()), out var table));
        var context = new VanillaNpcLootContext(row.GetProperty("expert").GetBoolean(), false, row.GetProperty("statue").GetBoolean())
        {
            DontStarveWorld = row.GetProperty("dontStarve").GetBoolean(),
            BloodMoonNight = row.GetProperty("bloodMoon").GetBoolean() && !row.GetProperty("day").GetBoolean(),
            WindyEnoughForKites = row.GetProperty("windy").GetBoolean()
        };
        Assert.True(VanillaNpcLootEvaluator.TryValidateNpcSpecificContext(in table, in context));
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var rolls = new Rolls(random, row.GetProperty("luck").GetSingle());
        var actual = new List<WorldItemDropStateUpdate>();
        var origin = new NpcLootWorldItemOrigin(640 + 47 / 2, 480 + 35 / 2);
        AssertRandom(row.GetProperty("before"), random);
        var checkpoints = row.GetProperty("checkpoints"); Assert.Equal(table.RuleCount, checkpoints.GetArrayLength());
        for (int i = 0; i < table.RuleCount; i++)
        {
            Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in table.Rules[i], in context, rolls, out bool dropped, out var drop));
            if (dropped)
            {
                Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, rolls, out var item));
                actual.Add(item);
            }
            AssertDrops(checkpoints[i].GetProperty("drops"), actual);
            AssertRandom(checkpoints[i].GetProperty("rng"), random);
        }
        AssertDrops(row.GetProperty("after").GetProperty("drops"), actual);
        AssertRandom(row.GetProperty("after").GetProperty("rng"), random);
    }

    [Theory]
    [InlineData(181)] [InlineData(464)] [InlineData(489)]
    public void Unknown_conditional_facts_refuse_entire_table_before_any_entropy(int type)
    {
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(type), out var table));
        var context = new VanillaNpcLootContext(false, false);
        Assert.False(VanillaNpcLootEvaluator.TryValidateNpcSpecificContext(in table, in context));
        Assert.False(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificTable(in table, in context,
            new RefuseDraws(), new NpcLootDrop[table.MaximumDropCount], out int count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void Missing_bad_luck_capability_refuses_instead_of_using_ordinary_luck()
    {
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(536), out var table));
        var context = new VanillaNpcLootContext(false, false);
        Assert.False(VanillaNpcLootEvaluator.TryEvaluateRule(in table.Rules[2], in context, new RefuseDraws(), out _, out _));
    }

    private static void AssertRule(JsonElement original, VanillaNpcLootRule rule)
    {
        if (rule.Kind == VanillaNpcLootRuleKind.FailedRollChain)
        {
            var alternatives = rule.Alternatives!.Rules;
            for (int i = 0; i < alternatives.Length; i++)
            {
                AssertLeaf(original, alternatives[i]);
                var chains = original.GetProperty("chains");
                Assert.Equal(i + 1 < alternatives.Length ? 1 : 0, chains.GetArrayLength());
                if (i + 1 < alternatives.Length)
                {
                    Assert.EndsWith("Chains+TryIfFailedRandomRoll", chains[0].GetProperty("kind").GetString());
                    original = chains[0].GetProperty("rule");
                }
            }
            return;
        }
        AssertLeaf(original, rule);
        Assert.Equal(0, original.GetProperty("chains").GetArrayLength());
    }

    private static void AssertLeaf(JsonElement original, VanillaNpcLootRule rule)
    {
        var fields = original.GetProperty("fields");
        if (rule.Kind == VanillaNpcLootRuleKind.ExpertGetsOneReroll || rule.NormalChanceDenominator != rule.ExpertChanceDenominator)
        {
            Assert.EndsWith(".DropBasedOnExpertMode", original.GetProperty("kind").GetString());
            Check(fields.GetProperty("ruleForNormalMode"), rule.NormalChanceDenominator, rule.Kind == VanillaNpcLootRuleKind.ExpertGetsOneReroll ? 1 : null);
            Check(fields.GetProperty("ruleForExpertMode"), rule.ExpertChanceDenominator, rule.Kind == VanillaNpcLootRuleKind.ExpertGetsOneReroll ? 2 : null);
        }
        else Check(original, rule.NormalChanceDenominator);

        void Check(JsonElement source, int chance, int? rerolls = null)
        {
            string shape = rule.Kind switch
            {
                VanillaNpcLootRuleKind.OneFromOptions => "OneFromOptionsDropRule",
                VanillaNpcLootRuleKind.OnlyBadLuckCommon => "CommonDropScalingWithOnlyBadLuck",
                VanillaNpcLootRuleKind.ExpertGetsOneReroll => "CommonDropWithRerolls",
                VanillaNpcLootRuleKind.NotFromStatueCommon or VanillaNpcLootRuleKind.DontStarveCommon or
                    VanillaNpcLootRuleKind.NotDontStarveCommon or VanillaNpcLootRuleKind.WindyCommon or
                    VanillaNpcLootRuleKind.BloodMoonNotFromStatueCommon => "ItemDropWithConditionRule",
                _ => "CommonDrop"
            };
            Assert.EndsWith("." + shape, source.GetProperty("kind").GetString());
            var facts = source.GetProperty("fields");
            Assert.Equal(chance, facts.GetProperty("chanceDenominator").GetInt32());
            Assert.Equal(1, facts.GetProperty("chanceNumerator").GetInt32());
            if (rerolls is { } count) Assert.Equal(count, facts.GetProperty("timesToRoll").GetInt32());
            if (rule.Kind == VanillaNpcLootRuleKind.OneFromOptions)
                Assert.Equal(rule.Options!.Items.ToArray().Select(t => t.Value), facts.GetProperty("dropIds").EnumerateArray().Select(v => v.GetInt32()));
            else
            {
                Assert.Equal(rule.ItemType.Value, facts.GetProperty("itemId").GetInt32());
                Assert.Equal(rule.MinimumStack, facts.GetProperty("amountDroppedMinimum").GetInt32());
                Assert.Equal(rule.MaximumStack, facts.GetProperty("amountDroppedMaximum").GetInt32());
            }
            string? condition = rule.Kind switch
            {
                VanillaNpcLootRuleKind.NotFromStatueCommon => "NotFromStatue",
                VanillaNpcLootRuleKind.DontStarveCommon => "DontStarveIsUp",
                VanillaNpcLootRuleKind.NotDontStarveCommon => "DontStarveIsNotUp",
                VanillaNpcLootRuleKind.WindyCommon => "WindyEnoughForKiteDrops",
                VanillaNpcLootRuleKind.BloodMoonNotFromStatueCommon => "IsBloodMoonAndNotFromStatue",
                _ => null
            };
            if (condition is not null) Assert.EndsWith("Conditions+" + condition, facts.GetProperty("condition").GetString());
        }
    }

    private static void AssertDrops(JsonElement expected, List<WorldItemDropStateUpdate> actual)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            var source = expected[i]; var item = actual[i];
            Assert.Equal((source.GetProperty("id").GetInt32(), source.GetProperty("stack").GetInt32(), source.GetProperty("prefix").GetInt32()),
                ((int)item.ItemNetId, (int)item.Stack, (int)item.Prefix));
            Assert.Equal(source.GetProperty("position").EnumerateArray().Select(v => v.GetString()), new[] { Bits(item.PositionX), Bits(item.PositionY) });
            Assert.Equal(source.GetProperty("velocity").EnumerateArray().Select(v => v.GetString()), new[] { Bits(item.VelocityX), Bits(item.VelocityY) });
        }
        static string Bits(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8");
    }

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 actual)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(actual)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(actual)!);
    }

    private sealed class Rolls(VanillaUnifiedRandom1458 random, float luck) : INpcLootRollSource
    {
        public int NextInt32(int min, int max) => random.Next(min, max);
        public int RollLuck(int range)
        {
            if (luck > 0f && (float)random.NextDouble() < luck) return random.Next(random.Next(range / 2, range));
            if (luck < 0f && (float)random.NextDouble() < -luck) return random.Next(random.Next(range, checked(range * 2)));
            return random.Next(range);
        }
        public bool TryRollOnlyBadLuck(int range, out int result)
        {
            result = luck < 0f && (float)random.NextDouble() < -luck
                ? random.Next(random.Next(range / 2, range)) : random.Next(range);
            return true;
        }
    }

    private sealed class RefuseDraws : INpcLootRollSource
    {
        public int RollLuck(int chanceDenominator) => throw new InvalidOperationException("Unexpected ordinary luck draw.");
        public int NextInt32(int min, int max) => throw new InvalidOperationException("Unexpected random draw.");
    }
}
