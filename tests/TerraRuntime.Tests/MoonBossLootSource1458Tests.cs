using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class MoonBossLootSource1458Tests
{
    private const string OriginalAssemblySha = "4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034";

    [Fact]
    public void Original_rule_tables_match_all_difficulties_waves_event_gates_and_recipient_censuses()
    {
        foreach (var (resource, expectedCount) in new[] { ("MoonBossRules1458", 92_160), ("MoonParticipantRules1458", 88_320) })
        {
            using var document = Load(resource);
            int count = 0;
            foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
            {
                var rolls = new Rolls(Key(row, "seed"));
                var sink = new RuleSink();
                var context = Context(row, Key(row, "active") != 0);
                Assert.True(VanillaMoonBossLootEvaluator1458.TryExecute(in context, new(650, 350),
                    Players(Key(row, "participants")), rolls, sink, out var result));
                int[][] expected = row.GetProperty("drops").EnumerateArray()
                    .Select(drop => drop.EnumerateArray().Select(value => value.GetInt32()).ToArray()).ToArray();
                int[][] actual = sink.Drops.GroupBy(drop => drop.ItemType.Value).OrderBy(group => group.Key)
                    .Select(group => new[] { group.Key, group.Sum(drop => drop.Stack) }).ToArray();
                Assert.Equal(expected.Length, actual.Length);
                for (int index = 0; index < expected.Length; index++) Assert.Equal(expected[index], actual[index]);
                Assert.Equal(sink.Drops.Count, result.WorldItemCount);
                Assert.Equal(row.GetProperty("value").GetSingle() == 0f, result.MoneyCleared);
                Assert.Equal(Key(row, "next"), rolls.Random.Next());
                count++;
            }
            Assert.Equal(expectedCount, count);
        }
    }

    [Fact]
    public void Original_physical_items_match_rule_order_prefixes_per_player_origins_launch_and_shared_rng()
    {
        foreach (var (resource, expectedCount) in new[] { ("MoonBossPhysical1458", 11_520), ("MoonParticipantPhysical1458", 88_320) })
        {
            using var document = Load(resource);
            int count = 0;
            bool sawEmptyCensusMoneyClear = false;
            bool sawFailedGateKeepingMoney = false;
            bool sawNonzeroPrefix = false;
            foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
            {
                var rolls = new Rolls(Key(row, "seed"));
                var sink = new PhysicalSink();
                var context = Context(row, !row.TryGetProperty("active", out var active) || active.GetInt32() != 0);
                Assert.True(VanillaMoonBossLootEvaluator1458.TryExecute(in context, new(650, 350),
                    Players(Key(row, "participants")), rolls, sink, out var result));
                var expected = row.GetProperty("drops").EnumerateArray().ToArray();
                Assert.Equal(expected.Length, result.WorldItemCount);
                Assert.Equal(expected.Length, sink.Items.Count);
                int pet = Key(row, "type") switch { 325 => 4793, 327 => 4812, 344 => 4813, 345 => 4814, _ => 4794 };
                Assert.Equal(expected.Count(drop => Key(drop, "type") == pet), result.MasterPetDropCount);
                for (int index = 0; index < expected.Length; index++)
                {
                    var item = sink.Items[index];
                    var source = expected[index];
                    Assert.Equal(index, Key(source, "slot"));
                    Assert.Equal(Key(source, "type"), item.ItemNetId);
                    Assert.Equal(Key(source, "stack"), item.Stack);
                    Assert.Equal(Key(source, "prefix"), item.Prefix);
                    Assert.Equal(Float(source, "x"), item.PositionX);
                    Assert.Equal(Float(source, "y"), item.PositionY);
                    Assert.Equal(Float(source, "vx"), item.VelocityX);
                    Assert.Equal(Float(source, "vy"), item.VelocityY);
                    Assert.Equal(WorldItemOwnershipMode.None, item.Ownership);
                    sawNonzeroPrefix |= item.Prefix != 0;
                }
                Assert.Equal(row.GetProperty("value").GetSingle() == 0f, result.MoneyCleared);
                Assert.Equal(Key(row, "next"), rolls.Random.Next());
                sawEmptyCensusMoneyClear |= Key(row, "participants") == 0 && result.MoneyCleared;
                sawFailedGateKeepingMoney |= Key(row, "mode") == 2 && !result.MoneyCleared;
                count++;
            }
            Assert.Equal(expectedCount, count);
            Assert.True(sawEmptyCensusMoneyClear);
            Assert.True(sawFailedGateKeepingMoney);
            Assert.True(sawNonzeroPrefix);
        }
    }

    [Fact]
    public void Original_item_defaults_prefix_families_stat_guards_and_natural_roll_cursors_match()
    {
        foreach (var (resource, expectedFacts, expectedPrefixes) in new[] { ("MoonBossItems1458", 44, 5_632), ("MoonParticipantItems1458", 52, 6_656) })
        {
            using var document = Load(resource);
            int factCount = 0;
            foreach (var fact in document.RootElement.GetProperty("facts").EnumerateArray())
            {
                var type = new ItemTypeId(Key(fact, "id"));
                Assert.True(VanillaDefinitionCatalog.TryGetRuntimeDefaults(type, out var defaults));
                Assert.Equal((Key(fact, "width"), Key(fact, "height"), Key(fact, "maxStack")),
                    (defaults.Width, defaults.Height, (int)defaults.MaximumStack));
                Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(type, out var drop));
                Assert.Equal(fact.GetProperty("noGravity").GetBoolean(), drop.NoGravity);
                Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(type));
                var prefixProperty = fact.GetProperty("prefixes");
                int[] expected = prefixProperty.ValueKind == JsonValueKind.Null ? [] : prefixProperty.EnumerateArray().Select(value => value.GetInt32()).ToArray();
                Assert.Equal(expected, VanillaItemPrefixCatalog.GetRollablePrefixes(drop.PrefixFamily).ToArray().Select(prefix => prefix.Value));
                var valid = fact.GetProperty("valid").EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
                foreach (int prefix in expected)
                    Assert.Equal(valid.Contains(prefix), VanillaItemPrefixCatalog.IsValidForItem(type, new(prefix)));
                factCount++;
            }
            Assert.Equal(expectedFacts, factCount);
            int count = 0;
            foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
            {
                var rolls = new Rolls(Key(row, "seed"));
                Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(new(Key(row, "id")), rolls, out var prefix));
                Assert.Equal(Key(row, "prefix"), prefix.Value);
                Assert.Equal(Key(row, "next"), rolls.Random.Next());
                count++;
            }
            Assert.Equal(expectedPrefixes, count);
        }
    }

    [Fact]
    public void Unknown_context_invalid_census_and_missing_delivery_refuse_before_any_rng_or_item()
    {
        var valid = new VanillaMoonBossLootContext1458(new(327), true, false, 15, true, true);
        foreach (var context in new[]
        {
            valid with { Type = new(328) }, valid with { PumpkinMoon = null }, valid with { WaveNumber = null },
            valid with { WaveNumber = 0 }, valid with { WaveNumber = 21 }, valid with { IsExpertMode = false }
        }) Refuses(context, []);
        Refuses(valid, [new(new(1), 10, 20), new(new(1), 30, 40)]);
        Refuses(valid, [new(new(255), 10, 20)]);
        Refuses(valid, [new(new(1), float.NaN, 20)]);
        Refuses(valid, Players(256));
        Refuses(new(new(341), false, true, 15, false, false), []);
        var rolls = new Rolls(77);
        var sink = new RuleSink { Unsupported = new(1785) };
        Assert.False(VanillaMoonBossLootEvaluator1458.TryExecute(in valid, new(10, 20), [], rolls, sink, out _));
        Assert.Equal(0, rolls.Calls);
        Assert.Empty(sink.Drops);
        // An inactive matching event needs no wave read and does not consume the special gate.
        valid = valid with { PumpkinMoon = false, WaveNumber = null };
        Assert.True(VanillaMoonBossLootEvaluator1458.TryExecute(in valid, new(10, 20), [], rolls, sink, out var result));
        Assert.Equal(default(MoonBossLootExecutionResult1458), result);
        Assert.Equal(0, rolls.Calls);
        var christmas = new VanillaMoonBossLootContext1458(new(341), false, false, null, false, false, true);
        Assert.True(VanillaMoonBossLootEvaluator1458.TryExecute(in christmas, new(10, 20), [], rolls, sink, out result));
        Assert.Equal(1869, Assert.Single(sink.Drops).ItemType.Value);
        Assert.False(result.MoneyCleared);
        Assert.Equal(2, rolls.Calls);
        void Refuses(VanillaMoonBossLootContext1458 context, VanillaMoonBossLootPlayer1458[] players)
        {
            var source = new Rolls(77);
            var target = new RuleSink();
            Assert.False(VanillaMoonBossLootEvaluator1458.TryExecute(in context, new(10, 20), players, source, target, out _));
            Assert.Equal(0, source.Calls);
            Assert.Empty(target.Drops);
        }
    }

    private static VanillaMoonBossLootContext1458 Context(JsonElement row, bool active)
    {
        int type = Key(row, "type"), mode = Key(row, "mode");
        return new(new(type), active && type < 338, active && type >= 338, Key(row, "wave"), mode > 0, mode == 2,
            row.TryGetProperty("christmas", out var christmas) ? christmas.GetBoolean() : null);
    }

    private static VanillaMoonBossLootPlayer1458[] Players(int count) => Enumerable.Range(0, count)
        .Select(slot => new VanillaMoonBossLootPlayer1458(new((byte)slot), 110 + slot * 100, 171 + slot * 50)).ToArray();
    private static int Key(JsonElement row, string key) => row.GetProperty(key).GetInt32();
    private static float Float(JsonElement row, string key) => row.GetProperty(key).GetSingle();
    private static JsonDocument Load(string name)
    {
        using var resource = typeof(MoonBossLootSource1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        var document = JsonDocument.Parse(gzip);
        Assert.Equal(OriginalAssemblySha, document.RootElement.GetProperty("source").GetString());
        return document;
    }

    private sealed class Rolls(int seed) : INpcLootRollSource
    {
        public VanillaUnifiedRandom1458 Random { get; } = new(seed);
        public int Calls { get; private set; }
        public int RollLuck(int chanceDenominator) { Calls++; return Random.Next(chanceDenominator); }
        public int NextInt32(int inclusiveMin, int exclusiveMax) { Calls++; return Random.Next(inclusiveMin, exclusiveMax); }
    }

    private sealed class RuleSink : IMoonBossLootDeliverySink1458
    {
        public ItemTypeId Unsupported { get; init; }
        public List<NpcLootDrop> Drops { get; } = [];
        public bool CanDeliverWorldItem(ItemTypeId type) => type != Unsupported;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        { Drops.Add(drop); return true; }
    }

    private sealed class PhysicalSink : IMoonBossLootDeliverySink1458
    {
        public List<WorldItemDropStateUpdate> Items { get; } = [];
        public bool CanDeliverWorldItem(ItemTypeId type) => VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(type);
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        {
            Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, random, out var item));
            Items.Add(item);
            return true;
        }
    }
}
