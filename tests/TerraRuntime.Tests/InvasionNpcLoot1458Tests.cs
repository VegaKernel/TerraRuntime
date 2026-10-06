using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class InvasionNpcLoot1458Tests
{
    private static JsonDocument Load()
    {
        using var stream = typeof(InvasionNpcLoot1458Tests).Assembly.GetManifestResourceStream("InvasionNpcLoot1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    [Fact]
    public void Ordered_tables_match_actual_populated_database_including_goblin_failed_roll_chain()
    {
        using var document = Load();
        foreach (var row in document.RootElement.GetProperty("tables").EnumerateArray())
        {
            Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(row.GetProperty("type").GetInt32()), out var table));
            var original = row.GetProperty("rules");
            Assert.Equal(original.GetArrayLength(), table.RuleCount);
            for (int index = 0; index < table.RuleCount; index++)
            {
                var rule = table.Rules[index];
                if (original[index].GetProperty("ChainedRules").GetArrayLength() != 0)
                {
                    Assert.Equal(VanillaNpcLootRuleKind.FailedRollChain, rule.Kind);
                    Assert.Equal(2, rule.Alternatives!.Rules.Length);
                    EqualRule(original[index], rule.Alternatives.Rules[0]);
                    EqualRule(original[index].GetProperty("ChainedRules")[0].GetProperty("RuleToChain"), rule.Alternatives.Rules[1]);
                }
                else EqualRule(original[index], rule);
            }
        }
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(212), out var pirate));
        Assert.Equal(34, pirate.MaximumDropCount);
        Assert.Equal(VanillaInvasionNpcLootCatalog1458.PirateMaximumDropCount, pirate.MaximumDropCount);
        Assert.True(VanillaInvasionNpcLootCatalog1458.TryGet(new(213), out var corsair));
        Assert.Equal(pirate.MaximumDropCount, corsair.MaximumDropCount);
    }

    private static void EqualRule(JsonElement original, in VanillaNpcLootRule rule)
    {
        Assert.Equal(VanillaNpcLootRuleKind.NormalVsExpertCommon, rule.Kind);
        Assert.Equal(original.GetProperty("itemId").GetInt32(), rule.ItemType.Value);
        Assert.Equal(original.GetProperty("chanceDenominator").GetInt32(), rule.NormalChanceDenominator);
        Assert.Equal(rule.NormalChanceDenominator, rule.ExpertChanceDenominator);
        Assert.Equal(original.GetProperty("amountDroppedMinimum").GetInt32(), rule.MinimumStack);
        Assert.Equal(original.GetProperty("amountDroppedMaximum").GetInt32(), rule.MaximumStack);
    }

    [Fact]
    public void Sparse_drop_defaults_and_natural_prefix_stream_match_actual_items_without_use_admission()
    {
        using var document = Load();
        foreach (var row in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var type = new ItemTypeId(row.GetProperty("id").GetInt32());
            Assert.True(VanillaInvasionDropCatalog1458.TryGet(type, out var sparse));
            var values = row.GetProperty("values");
            Assert.Equal((values.GetProperty("width").GetInt32(), values.GetProperty("height").GetInt32(), values.GetProperty("maxStack").GetInt32()),
                (sparse.RuntimeDefaults.Width, sparse.RuntimeDefaults.Height, (int)sparse.RuntimeDefaults.MaximumStack));
            Assert.Null(sparse.UseTiming);
            Assert.Null(sparse.Placement);
            Assert.Null(sparse.PickTool);
            Assert.Equal(values.GetProperty("noGravity").GetBoolean(), sparse.WorldDrop!.Value.NoGravity);
            Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(type, out _));
            var valid = row.GetProperty("validPrefixes").EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
            foreach (var prefix in VanillaItemPrefixCatalog.GetRollablePrefixes(sparse.WorldDrop.Value.PrefixFamily))
                Assert.Equal(valid.Contains(prefix.Value), VanillaItemPrefixCatalog.IsValidForItem(type, prefix));
            foreach (var roll in row.GetProperty("rolls").EnumerateArray())
            {
                var random = new VanillaUnifiedRandom1458(roll.GetProperty("seed").GetInt32());
                Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(type, new Rolls(random, 0f), out var prefix));
                Assert.Equal(roll.GetProperty("prefix").GetInt32(), prefix.Value);
                Assert.Equal(roll.GetProperty("next").GetInt32(), random.Next());
            }
        }
    }

    [Fact]
    public void Actual_specific_resolver_callbacks_match_physical_items_and_final_rng_with_immediate_materialization()
    {
        using var document = Load();
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            var rolls = new Rolls(random, row.GetProperty("luck").GetSingle());
            var context = new VanillaNpcLootContext(row.GetProperty("expert").GetBoolean(), false);
            Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(row.GetProperty("type").GetInt32()), out var table));
            var origin = new NpcLootWorldItemOrigin(800 + row.GetProperty("width").GetInt32() / 2,
                800 + row.GetProperty("height").GetInt32() / 2);
            var actual = new List<WorldItemDropStateUpdate>();
            foreach (ref readonly var rule in table.Rules)
            {
                Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in rule, in context, rolls, out bool dropped, out var drop));
                if (!dropped) continue;
                Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, rolls, out var item));
                actual.Add(item);
            }
            var expected = row.GetProperty("drops");
            Assert.Equal(expected.GetArrayLength(), actual.Count);
            for (int index = 0; index < actual.Count; index++)
            {
                var item = expected[index];
                var state = actual[index];
                Assert.Equal((item.GetProperty("id").GetInt32(), item.GetProperty("stack").GetInt32(), item.GetProperty("prefix").GetInt32()),
                    ((int)state.ItemNetId, (int)state.Stack, (int)state.Prefix));
                Assert.Equal((item.GetProperty("x").GetSingle(), item.GetProperty("y").GetSingle(), item.GetProperty("vx").GetSingle(), item.GetProperty("vy").GetSingle()),
                    (state.PositionX, state.PositionY, state.VelocityX, state.VelocityY));
            }
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        }
    }

    private sealed class Rolls(VanillaUnifiedRandom1458 random, float luck) : INpcLootRollSource
    {
        public int NextInt32(int minimum, int maximum) => random.Next(minimum, maximum);
        public int RollLuck(int range)
        {
            if (luck > 0f && (float)random.NextDouble() < luck) return random.Next(random.Next(range / 2, range));
            if (luck < 0f && (float)random.NextDouble() < -luck) return random.Next(random.Next(range, range * 2));
            return random.Next(range);
        }
    }
}
