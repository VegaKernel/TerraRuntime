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

public sealed class SlimeContainedLoot1458Tests
{
    public static IEnumerable<object[]> Defaults() => Rows("defaults");
    public static IEnumerable<object[]> Imported() => Rows("imported");
    public static IEnumerable<object[]> Direct() => Rows("direct");
    internal static IEnumerable<object[]> Rows(string kind)
    {
        using var stream = typeof(SlimeContainedLoot1458Tests).Assembly.GetManifestResourceStream(
            $"TerraRuntime.Tests.Fixtures.slime-contained-{kind}-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(Defaults))]
    public void Defaults_and_natural_prefix_match_original(JsonElement row)
    {
        var type = new ItemTypeId(row.GetProperty("type").GetInt32());
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(type, out var definition));
        Assert.Equal((row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32(),
            row.GetProperty("noGravity").GetBoolean()), (definition.Width, definition.Height, definition.NoGravity));
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(type, new Rolls(random), out var prefix));
        Assert.Equal(row.GetProperty("actualPrefix").GetInt32(), prefix.Value);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }

    [Theory, MemberData(nameof(Direct))]
    public void Direct_rule_materializes_original_stack_body_velocity_and_rng(JsonElement row) => Compare(row, false);

    [Theory, MemberData(nameof(Imported))]
    public void Global_content_precedes_complete_type_specific_original_chain(JsonElement row) => Compare(row, true);

    private static void Compare(JsonElement row, bool imported)
    {
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var rolls = new Rolls(random);
        var type = new NpcTypeId(row.GetProperty("type").GetInt32());
        var origin = new NpcLootWorldItemOrigin(1000 + row.GetProperty("width").GetInt32() / 2,
            1000 + row.GetProperty("height").GetInt32() / 2);
        var actual = new List<WorldItemDropStateUpdate>();
        Assert.True(VanillaSlimeBodyLoot1458.TryEvaluate(type, row.GetProperty("held").GetSingle(), rolls,
            out bool dropped, out var drop));
        Assert.True(dropped);
        Materialize(in origin, in drop, rolls, actual);
        if (imported && VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(type, out var table))
        {
            var context = new VanillaNpcLootContext(false, false);
            foreach (ref readonly var rule in table.Rules)
            {
                Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in rule, in context, rolls, out dropped, out drop));
                if (dropped) Materialize(in origin, in drop, rolls, actual);
            }
        }
        Assert.Equal(row.GetProperty("drops").GetArrayLength(), actual.Count);
        int index = 0;
        foreach (var expected in row.GetProperty("drops").EnumerateArray())
            Equal(expected, actual[index++]);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }

    internal static void Equal(JsonElement expected, in WorldItemDropStateUpdate actual)
    {
        Assert.Equal((expected.GetProperty("id").GetInt32(), expected.GetProperty("stack").GetInt32(),
            expected.GetProperty("prefix").GetInt32()), ((int)actual.ItemNetId, (int)actual.Stack, (int)actual.Prefix));
        Assert.Equal((expected.GetProperty("x").GetSingle(), expected.GetProperty("y").GetSingle(),
            expected.GetProperty("vx").GetSingle(), expected.GetProperty("vy").GetSingle()),
            (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
    }
    private static void Materialize(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, Rolls rolls,
        List<WorldItemDropStateUpdate> actual)
    {
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, rolls, out var item));
        actual.Add(item);
    }
    internal sealed class Rolls(VanillaUnifiedRandom1458 random) : INpcLootRollSource
    {
        public int NextInt32(int minimum, int maximum) => random.Next(minimum, maximum);
        public int RollLuck(int denominator) => random.Next(denominator);
    }
}
