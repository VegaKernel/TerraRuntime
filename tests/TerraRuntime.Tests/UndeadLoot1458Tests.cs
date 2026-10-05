using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;

namespace TerraRuntime.Tests;

public sealed class UndeadLoot1458Tests
{
    public static IEnumerable<object[]> Defaults() => Rows("defaults");
    public static IEnumerable<object[]> Whole() => Rows("whole");

    internal static IEnumerable<object[]> Rows(string kind)
    {
        using var stream = typeof(UndeadLoot1458Tests).Assembly.GetManifestResourceStream(
            $"TerraRuntime.Tests.Fixtures.undead-eleventh-{kind}-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Defaults))]
    public void Sparse_defaults_and_natural_prefix_match_original_without_item_use_admission(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var type = new ItemTypeId(row.GetProperty("type").GetInt32());
        Assert.True(VanillaUndeadDropCatalog1458.TryGet(type, out var definition));
        Assert.Equal((row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32(),
            row.GetProperty("maxStack").GetInt32()),
            (definition.RuntimeDefaults.Width, definition.RuntimeDefaults.Height, (int)definition.RuntimeDefaults.MaximumStack));
        Assert.True(VanillaUndeadDropCatalog1458.TryGet(type, out var sparse));
        Assert.Null(sparse.UseTiming); Assert.Null(sparse.Placement); Assert.Null(sparse.PickTool);
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), definition.WorldDrop!.Value.NoGravity);
        var family = VanillaUndeadDropCatalog1458.GetPrefixFamily(type);
        var expectedFamily = row.GetProperty("rollable");
        int[] expected = expectedFamily.ValueKind == JsonValueKind.Null ? [] :
            expectedFamily.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        Assert.Equal(expected, VanillaItemPrefixCatalog.GetRollablePrefixes(family).ToArray().Select(v => (int)v.Value));
        foreach (int prefix in expectedFamily.ValueKind == JsonValueKind.Null ? [] : expected.Append(0))
            Assert.Equal(row.GetProperty("validity")[prefix].GetBoolean(),
                VanillaItemPrefixCatalog.IsValidForItem(type, new PrefixId(prefix)));
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(type, new Rolls(random, 0f), out var actual));
        Assert.Equal(row.GetProperty("actualPrefix").GetInt32(), actual.Value);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }

    [Theory, MemberData(nameof(Whole))]
    public void Original_dedicated_hit_then_item_money_heal_callbacks_preserve_source_order(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement; var profile = row.GetProperty("profile");
        Assert.True(row.GetProperty("decompositionMatches").GetBoolean());
        var context = Context(row); var origin = Origin(context);
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var rolls = new Rolls(random, row.GetProperty("luck").GetSingle());
        rolls.Materializer = new VanillaNpcLootWorldItemMaterializer(() => new(context.Halloween!.Value, context.Christmas!.Value, profile.GetProperty("ExtraGel").GetBoolean()));
        var actual = new List<WorldItemDropStateUpdate>();
        if (context.NpcType.Value is 186 or 432) random.Next(2);
        Assert.Equal(row.GetProperty("hitNext").GetInt32(), random.Clone().Next());
        bool downed = profile.GetProperty("Mech").GetBoolean();
        var mech = new VanillaMechBossSpawnersContext1458(context.NpcValue, context.HardMode, downed, downed, downed);
        Assert.True(VanillaMechBossSpawners1458.TryEvaluate(in mech, rolls, out bool dropped, out var drop));
        if (dropped) Materialize(in origin, in drop, rolls, actual);
        Checkpoint(0);
        Assert.True(VanillaSlimeBodyLoot1458.TryEvaluate(context.NpcType, profile.GetProperty("Held").GetSingle(),
            rolls, out dropped, out drop));
        if (dropped) Materialize(in origin, in drop, rolls, actual);
        Checkpoint(1);
        for (int index = 0; index < VanillaNpcGlobalLoot1458.RuleCount; index++)
        {
            Assert.True(VanillaNpcGlobalLoot1458.TryGetEligibility(index, in context, out bool eligible));
            Assert.Equal(row.GetProperty("eligibility")[index + 2].GetBoolean(), eligible);
            Assert.True(VanillaNpcGlobalLoot1458.TryEvaluateRule(index, in context, rolls, out dropped, out drop));
            if (dropped) Materialize(in origin, in drop, rolls, actual);
            Checkpoint(index + 2);
        }
        EqualDrops(row.GetProperty("globalDrops"), actual);

        if (VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(context.NpcType, out var table))
        {
            bool expert = context.Difficulty >= 2f;
            int bag = profile.GetProperty("SickleCase").GetInt32();
            var specific = new VanillaNpcLootContext(expert, profile.GetProperty("ExtraGel").GetBoolean(), profile.GetProperty("Statue").GetBoolean(),
                profile.GetProperty("LowTiles").GetBoolean(), bag is 1 or 3);
            Assert.True(VanillaNpcLootEvaluator.TryValidateNpcSpecificContext(in table, in specific));
            foreach (ref readonly var rule in table.Rules)
            {
                Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in rule, in specific, rolls, out dropped, out drop));
                if (dropped) Materialize(in origin, in drop, rolls, actual);
            }
        }
        EqualDrops(row.GetProperty("importedDrops"), actual);
        Assert.Equal(row.GetProperty("importedNext").GetInt32(), random.Clone().Next());
        var sink = new Sink(origin, rolls, actual);
        var money = new VanillaNpcMoneyContext1458(context.NpcValue, 0, rolls.Luck, false, false);
        Assert.True(VanillaNpcMoneyLoot1458.TryPlan(in money, rolls, sink, 400));
        EqualDrops(row.GetProperty("moneyDrops"), actual);
        Assert.Equal(row.GetProperty("moneyNext").GetInt32(), random.Clone().Next());
        bool injured = row.GetProperty("injured").GetBoolean();
        var healing = new VanillaNpcHealingContext1458(context.NpcType, new NpcNetId(context.NpcType.Value),
            context.NpcLifeMax, context.NpcDamage, injured, injured, context.Difficulty >= 2f);
        Assert.True(VanillaNpcHealingLoot1458.TryExecute(in healing, in origin, rolls, sink));
        EqualDrops(row.GetProperty("drops"), actual);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        void Checkpoint(int index)
        {
            Assert.Equal(row.GetProperty("globalCounts")[index].GetInt32(), actual.Count);
            Assert.Equal(row.GetProperty("globalCheckpoints")[index].GetInt32(), random.Clone().Next());
        }
    }

    [Theory]
    [InlineData(21, 3)]
    [InlineData(201, 3)]
    [InlineData(202, 3)]
    [InlineData(203, 3)]
    [InlineData(322, 3)]
    [InlineData(323, 3)]
    [InlineData(324, 3)]
    [InlineData(635, 2)]
    [InlineData(449, 3)]
    [InlineData(450, 3)]
    [InlineData(451, 3)]
    [InlineData(452, 3)]
    [InlineData(31, 4)]
    [InlineData(32, 5)]
    [InlineData(34, 4)]
    [InlineData(294, 4)]
    [InlineData(295, 4)]
    [InlineData(296, 4)]
    [InlineData(693, 4)]
    [InlineData(110, 2)]
    [InlineData(3, 4)]
    [InlineData(591, 5)]
    [InlineData(590, 5)]
    [InlineData(331, 4)]
    [InlineData(332, 4)]
    [InlineData(132, 4)]
    [InlineData(161, 5)]
    [InlineData(186, 5)]
    [InlineData(187, 6)]
    [InlineData(188, 5)]
    [InlineData(189, 5)]
    [InlineData(200, 4)]
    [InlineData(223, 6)]
    [InlineData(319, 4)]
    [InlineData(320, 4)]
    [InlineData(321, 4)]
    [InlineData(430, 4)]
    [InlineData(431, 5)]
    [InlineData(432, 5)]
    [InlineData(433, 6)]
    [InlineData(434, 5)]
    [InlineData(435, 5)]
    [InlineData(436, 4)]
    public void All_registered_undead_tables_match_original_top_level_counts(int type, int count)
    {
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new NpcTypeId(type), out var table));
        Assert.Equal(count, table.RuleCount);
        Assert.InRange(table.MaximumDropCount, 1, 6);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(true, null)]
    [InlineData(null, false)]
    public void Unowned_Sickle_condition_rejects_entire_Zombie_table_before_any_random_draw(bool? lowTiles, bool? hasSickle)
    {
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new NpcTypeId(3), out var table));
        var context = new VanillaNpcLootContext(false, false, LowTiles: lowTiles, HasSickle: hasSickle);
        Assert.False(VanillaNpcLootEvaluator.TryValidateNpcSpecificContext(in table, in context));
        Assert.False(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificTable(in table, in context,
            new RejectDraw(), new NpcLootDrop[4], out int count));
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Proven_ineligible_Sickle_does_not_require_other_fact_or_consume_random(bool? lowTiles, bool? hasSickle)
    {
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new NpcTypeId(3), out var table));
        var context = new VanillaNpcLootContext(false, false, LowTiles: lowTiles, HasSickle: hasSickle);
        Assert.True(VanillaNpcLootEvaluator.TryValidateNpcSpecificContext(in table, in context));
        Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in table.Rules[3], in context,
            new RejectDraw(), out bool dropped, out _));
        Assert.False(dropped);
    }

    [Fact]
    public void Verified_empty_Slimer_is_distinct_from_unknown_type_and_consumes_no_table_random()
    {
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new NpcTypeId(121), out var table));
        var context = new VanillaNpcLootContext(false, false);
        Assert.True(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificTable(in table, in context,
            new RejectDraw(), [], out int count));
        Assert.Equal(0, count);
        Assert.False(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new NpcTypeId(32767), out _));
    }

    private sealed class RejectDraw : INpcLootRollSource
    {
        public int RollLuck(int range) => throw new InvalidOperationException("Unowned condition consumed luck RNG.");
        public int NextInt32(int minimum, int maximum) => throw new InvalidOperationException("Unowned condition consumed stack RNG.");
    }

    internal static VanillaNpcGlobalLootContext1458 Context(JsonElement row)
    {
        var p = row.GetProperty("profile"); int bits = p.GetProperty("Zones").GetInt32();
        byte zone1 = (byte)(((bits & 1) != 0 ? 16 : 0) | ((bits & 2) != 0 ? 2 : 0) |
            ((bits & 4) != 0 ? 64 : 0) | ((bits & 8) != 0 ? 4 : 0) |
            ((bits & 16) != 0 ? 32 : 0) | ((bits & 128) != 0 ? 1 : 0));
        return new(new NpcTypeId(p.GetProperty("Type").GetInt32()), p.GetProperty("Value").GetSingle(),
            p.GetProperty("Damage").GetInt32(), p.GetProperty("Defense").GetInt32(), p.GetProperty("Life").GetInt32(),
            p.GetProperty("Friendly").GetBoolean(), p.GetProperty("Boss").GetBoolean(), p.GetProperty("Target").GetInt32(),
            p.GetProperty("X").GetSingle(), p.GetProperty("Y").GetSingle(), 47, row.GetProperty("difficulty").GetSingle(),
            4200, 1200, p.GetProperty("Hard").GetBoolean(), p.GetProperty("Remix").GetBoolean(),
            p.GetProperty("Simulation").GetBoolean())
        {
            Halloween = p.GetProperty("Halloween").GetBoolean(), Christmas = p.GetProperty("Xmas").GetBoolean(),
            Zones = new(zone1, (byte)((bits & 32) != 0 ? 32 : 0), (byte)((bits & 64) != 0 ? 32 : 0), 0, 0, 0),
            RockLayer = 200d, WorldSurface = 140d, SkeletronDowned = p.GetProperty("Skeletron").GetBoolean(),
            AnyMechDowned = p.GetProperty("Mech").GetBoolean()
        };
    }

    private static NpcLootWorldItemOrigin Origin(in VanillaNpcGlobalLootContext1458 c) =>
        new((int)c.PositionX + c.NpcWidth / 2, (int)c.PositionY + 35 / 2);
    internal static void EqualDrops(JsonElement expected, List<WorldItemDropStateUpdate> actual)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Count); int index = 0;
        foreach (var item in expected.EnumerateArray())
        {
            var state = actual[index++];
            Assert.Equal((item.GetProperty("id").GetInt32(), item.GetProperty("stack").GetInt32(), item.GetProperty("prefix").GetInt32()),
                ((int)state.ItemNetId, (int)state.Stack, (int)state.Prefix));
            Assert.Equal((item.GetProperty("x").GetSingle(), item.GetProperty("y").GetSingle(),
                item.GetProperty("vx").GetSingle(), item.GetProperty("vy").GetSingle()),
                (state.PositionX, state.PositionY, state.VelocityX, state.VelocityY));
        }
    }
    private static void Materialize(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, Rolls rolls,
        List<WorldItemDropStateUpdate> actual)
    {
        Assert.True(rolls.Materializer.TryMaterialize(in origin, in drop, rolls, out var item));
        actual.Add(item);
    }

    internal sealed class Rolls(VanillaUnifiedRandom1458 random, float luck) : INpcLootRollSource, INpcMoneyRandom1458
    {
        public float Luck => luck;
        public VanillaNpcLootWorldItemMaterializer Materializer { get; set; } = VanillaNpcLootWorldItemMaterializer.Instance;
        public int NextInt32(int minimum, int maximum) => random.Next(minimum, maximum);
        public float NextFloat() => (float)random.NextDouble();
        public int RollLuck(int range)
        {
            if (luck > 0f && NextFloat() < luck) return random.Next(random.Next(range / 2, range));
            if (luck < 0f && NextFloat() < -luck) return random.Next(random.Next(range, range * 2));
            return random.Next(range);
        }
    }
    private sealed class Sink(NpcLootWorldItemOrigin origin, Rolls rolls, List<WorldItemDropStateUpdate> actual)
        : INpcMoneyPlanningSink1458, IBossRecoveryLootDeliverySink1458
    {
        public bool CanDeliverWorldItem(ItemTypeId type) => rolls.Materializer.CanMaterialize(type);
        public bool TryMaterialize(ItemTypeId type, int stack, INpcMoneyRandom1458 random)
        { var drop = new NpcLootDrop(type, checked((short)stack)); Materialize(in origin, in drop, rolls, actual); return true; }
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin at, in NpcLootDrop drop, INpcLootRollSource random)
        { Materialize(in at, in drop, rolls, actual); return true; }
    }
}
