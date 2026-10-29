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

public sealed class RangedCohortLoot1458Tests
{
    internal static IEnumerable<JsonElement> Rows(string group, string field)
    {
        using var stream = typeof(RangedCohortLoot1458Tests).Assembly.GetManifestResourceStream("RangedCohortDeathLootOriginal1458.v2")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("groups").GetProperty(group).GetProperty(field).EnumerateArray()) yield return row.Clone();
    }
    public static IEnumerable<object[]> Graphs() => new[] { "parentSpecificLoot", "childSpecificLoot" }
        .SelectMany(g => Rows(g, "graphs")).Select(r => new object[] { r.GetRawText() });
    public static IEnumerable<object[]> Drops() => new[] { "parentSpecificLoot", "parentRarePositiveLoot", "childSpecificLoot", "childRarePositiveLoot" }
        .SelectMany(g => Rows(g, "cases")).Select(r => new object[] { r.GetRawText() });

    [Theory, MemberData(nameof(Graphs))]
    public void Actual_populated_specific_graph_order_distinguishes_uniform_costume_and_empty_mount(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(row.GetProperty("type").GetInt32()), out var table));
        var rules = row.GetProperty("rules"); Assert.Equal(rules.GetArrayLength(), table.RuleCount);
        for (int i = 0; i < table.RuleCount; i++)
        {
            Assert.EndsWith(".CommonDrop", rules[i].GetProperty("kind").GetString());
            Assert.Empty(rules[i].GetProperty("chains").EnumerateArray());
            var f = rules[i].GetProperty("fields"); var actual = table.Rules[i];
            Assert.Equal(VanillaNpcLootRuleKind.NormalVsExpertCommon, actual.Kind);
            Assert.Equal(f.GetProperty("itemId").GetInt32(), actual.ItemType.Value);
            Assert.Equal(f.GetProperty("chanceDenominator").GetInt32(), actual.NormalChanceDenominator);
            Assert.Equal(actual.NormalChanceDenominator, actual.ExpertChanceDenominator);
            Assert.Equal(f.GetProperty("amountDroppedMinimum").GetInt32(), actual.MinimumStack);
            Assert.Equal(f.GetProperty("amountDroppedMaximum").GetInt32(), actual.MaximumStack);
        }
    }

    [Theory, MemberData(nameof(Drops))]
    public void Real_rule_boundaries_and_materialized_stack_prefix_motion_match_complete_original_rng(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(row.GetProperty("type").GetInt32()), out var table));
        var context = new VanillaNpcLootContext(row.GetProperty("expert").GetBoolean(), false,
            row.GetProperty("statue").GetBoolean());
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

    [Fact]
    public void Independently_observed_reward_sizes_and_all_nine_parent_rare_rewards_are_materializable()
    {
        foreach (string group in new[] { "parentSpecificLoot", "childSpecificLoot" })
            foreach (var row in Rows(group, "defaults"))
            {
                var type = new ItemTypeId(row.GetProperty("id").GetInt32());
                Assert.True(TerraRuntime.Gameplay.Items.VanillaDefinitionCatalog.TryGet(type, out var item));
                Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(type));
                Assert.Equal(row.GetProperty("width").GetInt32(), item.RuntimeDefaults.Width);
                Assert.Equal(row.GetProperty("height").GetInt32(), item.RuntimeDefaults.Height);
                Assert.Equal(row.GetProperty("maxStack").GetInt32(), item.RuntimeDefaults.MaximumStack);
                Assert.True(TerraRuntime.Gameplay.Items.VanillaDefinitionCatalog.TryGetWorldDrop(type, out var worldDrop));
                Assert.Equal(row.GetProperty("noGravity").GetBoolean(), worldDrop.NoGravity);
            }
        Assert.Equal(new[] { 3, 2798, 2800, 2806, 2807, 2808, 2860, 2882, 4761 },
            Rows("parentRarePositiveLoot", "cases").SelectMany(r => r.GetProperty("newlyCovered").EnumerateArray().Select(v => v.GetInt32())).Order());
    }

    internal static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 actual)
    {
        const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(actual)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(actual)!);
    }
    private static void AssertDrops(JsonElement expected, List<WorldItemDropStateUpdate> actual)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            var e = expected[i]; var a = actual[i];
            Assert.Equal((e.GetProperty("id").GetInt32(), e.GetProperty("stack").GetInt32(), e.GetProperty("prefix").GetInt32()), ((int)a.ItemNetId, (int)a.Stack, (int)a.Prefix));
            Assert.Equal(e.GetProperty("position").EnumerateArray().Select(v => v.GetString()), new[] { Bits(a.PositionX), Bits(a.PositionY) });
            Assert.Equal(e.GetProperty("velocity").EnumerateArray().Select(v => v.GetString()), new[] { Bits(a.VelocityX), Bits(a.VelocityY) });
        }
    }
    internal static string Bits(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8");
    private sealed class Rolls(VanillaUnifiedRandom1458 random, float luck) : INpcLootRollSource
    {
        public int NextInt32(int min, int max) => random.Next(min, max);
        public int RollLuck(int range)
        {
            if (luck > 0f && (float)random.NextDouble() < luck) return random.Next(random.Next(range / 2, range));
            if (luck < 0f && (float)random.NextDouble() < -luck) return random.Next(random.Next(range, checked(range * 2)));
            return random.Next(range);
        }
    }
}
