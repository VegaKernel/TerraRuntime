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

public sealed class PirateNpcLoot1458Tests
{
    private static JsonDocument Load()
    {
        using var stream = typeof(PirateNpcLoot1458Tests).Assembly.GetManifestResourceStream("PirateNpcLoot1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    [Fact]
    public void Populated_source_tables_preserve_shared_order_captain_rules_and_explicit_empty_tables()
    {
        using var document = Load();
        foreach (var row in document.RootElement.GetProperty("tables").EnumerateArray())
        {
            int type = row.GetProperty("type").GetInt32();
            var original = row.GetProperty("rules");
            if (type is 491 or 492)
            {
                Assert.Equal(type == 491 ? 12 : 0, original.GetArrayLength());
                Assert.False(VanillaInvasionNpcLootCatalog1458.TryGet(new(type), out _));
                continue;
            }

            Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(type), out var table));
            Assert.Equal(original.GetArrayLength(), table.RuleCount);
            Assert.Equal(type is >= 212 and <= 215 ? 34 : type == 216 ? 7 : 0, table.MaximumDropCount);
            for (int index = 0; index < table.RuleCount; index++)
            {
                var rule = table.Rules[index];
                Assert.Equal(VanillaNpcLootRuleKind.NormalVsExpertCommon, rule.Kind);
                Assert.Equal(original[index].GetProperty("itemId").GetInt32(), rule.ItemType.Value);
                Assert.Equal(original[index].GetProperty("chanceDenominator").GetInt32(), rule.NormalChanceDenominator);
                Assert.Equal(rule.NormalChanceDenominator, rule.ExpertChanceDenominator);
                Assert.Equal(original[index].GetProperty("amountDroppedMinimum").GetInt32(), rule.MinimumStack);
                Assert.Equal(original[index].GetProperty("amountDroppedMaximum").GetInt32(), rule.MaximumStack);
            }
        }
    }

    [Fact]
    public void Actual_specific_callbacks_materialize_each_item_before_the_next_roll_and_preserve_cursor()
    {
        using var document = Load();
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            var rolls = new Rolls(random);
            var context = new VanillaNpcLootContext(row.GetProperty("expert").GetBoolean(), false);
            Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(row.GetProperty("type").GetInt32()), out var table));
            var origin = new NpcLootWorldItemOrigin(800 + row.GetProperty("width").GetInt32() / 2,
                800 + row.GetProperty("height").GetInt32() / 2);
            var actual = new List<WorldItemDropStateUpdate>();
            foreach (ref readonly var rule in table.Rules)
            {
                Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in rule, in context, rolls, out bool dropped, out var drop));
                if (!dropped)
                    continue;
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

    [Fact]
    public void Every_captain_output_reuses_verified_sparse_defaults_and_natural_prefix_facts()
    {
        using var document = Load();
        var captured = document.RootElement.GetProperty("rows").EnumerateArray()
            .Where(row => row.GetProperty("type").GetInt32() == 216)
            .SelectMany(row => row.GetProperty("drops").EnumerateArray())
            .Select(row => row.GetProperty("id").GetInt32()).ToHashSet();
        int[] outputs = [905, 855, 854, 2584, 3033, 672, 5460];
        Assert.True(captured.SetEquals(outputs));
        foreach (var row in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var type = new ItemTypeId(row.GetProperty("id").GetInt32());
            Assert.True(VanillaInvasionDropCatalog1458.TryGet(type, out var definition));
            var values = row.GetProperty("values");
            Assert.Equal((values.GetProperty("width").GetInt32(), values.GetProperty("height").GetInt32(), values.GetProperty("maxStack").GetInt32()),
                (definition.RuntimeDefaults.Width, definition.RuntimeDefaults.Height, (int)definition.RuntimeDefaults.MaximumStack));
            Assert.Null(definition.UseTiming);
            foreach (var offer in row.GetProperty("rolls").EnumerateArray())
            {
                var random = new VanillaUnifiedRandom1458(offer.GetProperty("seed").GetInt32());
                Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(type, new Rolls(random), out var prefix));
                Assert.Equal(offer.GetProperty("prefix").GetInt32(), prefix.Value);
                Assert.Equal(offer.GetProperty("next").GetInt32(), random.Next());
            }
        }
    }

    private sealed class Rolls(VanillaUnifiedRandom1458 random) : INpcLootRollSource
    {
        public int NextInt32(int minimum, int maximum) => random.Next(minimum, maximum);
        public int RollLuck(int range) => random.Next(range);
    }
}
