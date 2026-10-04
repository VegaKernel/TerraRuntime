using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class MechSpawners1458Tests
{
    public static IEnumerable<object[]> OriginalDefaults()
    {
        using var stream = typeof(MechSpawners1458Tests).Assembly.GetManifestResourceStream("MechSpawnersDefaults1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalDefaults))]
    public void Original_summon_defaults_and_natural_prefix_rng(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var type = new TerraRuntime.Contracts.Gameplay.ItemTypeId(row.GetProperty("type").GetInt32());
        Assert.True(VanillaDefinitionCatalog.TryGet(type, out var definition));
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(type, out var drop));
        Assert.Equal(row.GetProperty("width").GetInt32(), definition.RuntimeDefaults.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), definition.RuntimeDefaults.Height);
        Assert.Equal(row.GetProperty("maxStack").GetInt32(), definition.RuntimeDefaults.MaximumStack);
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), drop.NoGravity);
        Assert.Null(definition.UseTiming);
        var rolls = new Rolls(row.GetProperty("seed").GetInt32(), 0f);
        Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(type, rolls, out var prefix));
        Assert.Equal(row.GetProperty("actualPrefix").GetInt32(), prefix.Value);
        Assert.Equal(row.GetProperty("next").GetInt32(), rolls.Source.Next());
    }

    public static IEnumerable<object[]> OriginalRows()
    {
        using var stream = typeof(MechSpawners1458Tests).Assembly.GetManifestResourceStream("MechSpawners1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Original_global_offers_materialization_and_next_rng(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        int mask = row.GetProperty("mask").GetInt32();
        var context = new VanillaMechBossSpawnersContext1458(row.GetProperty("positive").GetBoolean() ? 25f : 0f,
            row.GetProperty("hard").GetBoolean(), (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
            row.GetProperty("simulation").GetBoolean());
        Assert.Equal(row.GetProperty("eligible").GetBoolean(), context.CanDrop);
        var rolls = new Rolls(row.GetProperty("seed").GetInt32(), row.GetProperty("luck").GetSingle());
        Assert.True(VanillaMechBossSpawners1458.TryEvaluate(in context, rolls, out bool dropped, out var drop));
        var expected = row.GetProperty("drops").EnumerateArray().ToArray();
        Assert.Equal(expected.Length != 0, dropped);
        if (dropped)
        {
            int body = row.GetProperty("body").GetInt32();
            var origin = new NpcLootWorldItemOrigin(16000 + (body == 0 ? 24 : 47) / 2, 1000 + (body == 0 ? 18 : 35) / 2);
            Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, rolls, out var actual));
            AssertDrop(expected.Single(), in actual);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), rolls.Source.Next());
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Original_full_BlueSlime_chain_keeps_global_before_contents_and_Gel(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        int mask = row.GetProperty("mask").GetInt32();
        // NPCLoot_DropItems builds a live DropAttemptInfo, independently of the direct-rule simulation flag.
        var context = new VanillaMechBossSpawnersContext1458(row.GetProperty("positive").GetBoolean() ? 25f : 0f,
            row.GetProperty("hard").GetBoolean(), (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
        var rolls = new Rolls(row.GetProperty("seed").GetInt32(), row.GetProperty("luck").GetSingle());
        int body = row.GetProperty("body").GetInt32();
        var origin = new NpcLootWorldItemOrigin(16000 + (body == 0 ? 24 : 47) / 2, 1000 + (body == 0 ? 18 : 35) / 2);
        var drops = new List<WorldItemDropStateUpdate>();
        Assert.True(VanillaMechBossSpawners1458.TryEvaluate(in context, rolls, out bool dropped, out var drop));
        if (dropped) Add(in origin, in drop, rolls, drops);
        Assert.True(VanillaSlimeBodyLoot1458.TryEvaluate(new(1), 2f, rolls, out dropped, out drop));
        Assert.True(dropped); Add(in origin, in drop, rolls, drops);
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(1), out var table));
        var lootContext = new VanillaNpcLootContext(false, false);
        foreach (var rule in table.Rules)
        {
            Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in rule, in lootContext, rolls, out dropped, out drop));
            if (dropped) Add(in origin, in drop, rolls, drops);
        }
        var expected = row.GetProperty("fullDrops").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, drops.Count);
        for (int i = 0; i < drops.Count; i++) { var actual = drops[i]; AssertDrop(expected[i], in actual); }
        Assert.Equal(row.GetProperty("fullNext").GetInt32(), rolls.Source.Next());
    }

    private static void Add(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, Rolls rolls, List<WorldItemDropStateUpdate> drops)
    {
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, rolls, out var actual));
        drops.Add(actual);
    }

    private static void AssertDrop(JsonElement row, in WorldItemDropStateUpdate actual)
    {
        Assert.Equal(row.GetProperty("id").GetInt32(), actual.ItemNetId);
        Assert.Equal(row.GetProperty("stack").GetInt32(), actual.Stack);
        Assert.Equal(row.GetProperty("prefix").GetInt32(), actual.Prefix);
        Assert.Equal(row.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
    }

    private sealed class Rolls(int seed, float luck) : INpcLootRollSource
    {
        public VanillaUnifiedRandom1458 Source { get; } = new(seed);
        public int NextInt32(int min, int max) => Source.Next(min, max);
        public int RollLuck(int denominator)
        {
            if (luck > 0 && (float)Source.NextDouble() < luck) return Source.Next(Source.Next(denominator / 2, denominator));
            if (luck < 0 && (float)Source.NextDouble() < -luck) return Source.Next(Source.Next(denominator, denominator * 2));
            return Source.Next(denominator);
        }
    }
}
