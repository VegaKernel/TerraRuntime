using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;

namespace TerraRuntime.Tests;

public sealed class NpcMoney1458Tests
{
    // Original 1.4.5.8 private NPCLoot_DropMoney on real NPC/Player/WorldItem instances.
    public static IEnumerable<object[]> MoneyRows() => Rows("npc-money-official-1458");
    public static IEnumerable<object[]> DefaultsRows() => Rows("npc-money-defaults-official-1458");
    public static IEnumerable<object[]> VariantRows() => Rows("npc-money-variants-official-1458");

    [Theory, MemberData(nameof(MoneyRows))]
    public void Exact_original_coin_materialization_and_next_rng(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json); var row = document.RootElement;
        var random = new MoneyRandom(row.GetProperty("seed").GetInt32()); var sink = new MaterializedCoins();
        var context = new VanillaNpcMoneyContext1458(row.GetProperty("value").GetSingle(), row.GetProperty("extraValue").GetInt32(),
            row.GetProperty("luck").GetSingle(), row.GetProperty("midas").GetBoolean(), row.GetProperty("bloodMoon").GetBoolean());
        Assert.True(VanillaNpcMoneyLoot1458.TryPlan(in context, random, sink, 4096));
        var expected = row.GetProperty("drops").EnumerateArray().ToArray(); Assert.Equal(expected.Length, sink.Drops.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            var actual = sink.Drops[index]; var original = expected[index];
            Assert.Equal(original.GetProperty("id").GetInt32(), actual.ItemNetId);
            Assert.Equal(original.GetProperty("stack").GetInt32(), actual.Stack);
            Assert.Equal(original.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(original.GetProperty("vy").GetSingle(), actual.VelocityY);
            Assert.Equal(original.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(original.GetProperty("y").GetSingle(), actual.PositionY);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Source.Next());
    }

    [Theory, MemberData(nameof(DefaultsRows))]
    public void Spawn_value_matches_original_SetDefaults_and_source_strength(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json); var row = document.RootElement;
        int type = row.GetProperty("type").GetInt32(); float difficulty = row.GetProperty("difficulty").GetSingle();
        Assert.True(VanillaNpcMoneyDefaults1458.TryResolve(new(type), new((short)type), difficulty, out float value));
        Assert.Equal(row.GetProperty("value").GetSingle(), value);
        var store = new RuntimeNpcStore(1);
        var update = new NpcStateUpdate(type, (short)type, 100, 200, 0, 0, 255, default,
            NpcSimulationState.Initial with { SpawnDifficulty = difficulty });
        Assert.True(store.TrySpawn(0, in update, out var npc)); Assert.Equal(value, npc.Simulation.MoneyValue);
        Assert.Equal(0, npc.Simulation.ExtraMoneyValue); Assert.False(npc.Simulation.Midas);
    }

    [Theory, MemberData(nameof(VariantRows))]
    public void Negative_variant_integer_order_matches_original(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.True(VanillaNpcMoneyDefaults1458.TryResolve(new(row.GetProperty("type").GetInt32()), new(row.GetProperty("netId").GetInt16()),
            row.GetProperty("difficulty").GetSingle(), out float amount));
        Assert.Equal(row.GetProperty("value").GetSingle(), amount);
    }

    [Fact]
    public void State_only_update_preserves_owned_value_additions_and_Midas()
    {
        var store = new RuntimeNpcStore(1);
        var update = new NpcStateUpdate(VanillaNpcIds.BlueSlime.Value, (short)VanillaNpcIds.BlueSlime.Value, 100, 200, 0, 0, 255, default,
            NpcSimulationState.Initial with { MoneyValue = 0f, ExtraMoneyValue = 12345, Midas = true });
        Assert.True(store.TrySpawn(0, in update, out var npc)); update = update with { Simulation = NpcSimulationState.Initial };
        Assert.True(store.TryUpdate(npc.Handle, in update, out var after));
        Assert.Equal(0f, after.Simulation.MoneyValue); Assert.Equal(12345, after.Simulation.ExtraMoneyValue); Assert.True(after.Simulation.Midas);
    }

    [Fact]
    public void Bounded_split_rejection_preserves_live_rng_when_preview_is_discarded()
    {
        var live = new VanillaUnifiedRandom1458(1458); var before = live.Clone();
        var preview = new MoneyRandom(live.Clone()); var context = new VanillaNpcMoneyContext1458(100f, 0, 0, false, false);
        Assert.False(VanillaNpcMoneyLoot1458.TryPlan(in context, preview, new MaterializedCoins(), 1));
        Assert.True(live.HasSameState(before));
    }

    private static IEnumerable<object[]> Rows(string name)
    {
        using var resource = typeof(NpcMoney1458Tests).Assembly.GetManifestResourceStream($"TerraRuntime.Tests.Fixtures.{name}.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress); using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }
    private sealed class MoneyRandom : INpcMoneyRandom1458, INpcLootRollSource
    {
        public VanillaUnifiedRandom1458 Source { get; }
        public MoneyRandom(int seed) : this(new VanillaUnifiedRandom1458(seed)) { }
        public MoneyRandom(VanillaUnifiedRandom1458 source) => Source = source;
        public int NextInt32(int min, int max) => Source.Next(min, max);
        public float NextFloat() => (float)Source.NextDouble();
        public int RollLuck(int chanceDenominator) => Source.Next(chanceDenominator);
    }
    private sealed class MaterializedCoins : INpcMoneyPlanningSink1458
    {
        public List<WorldItemDropStateUpdate> Drops { get; } = [];
        public bool TryMaterialize(ItemTypeId type, int stack, INpcMoneyRandom1458 random)
        {
            var origin = new NpcLootWorldItemOrigin(1050, 1060); var drop = new NpcLootDrop(type, checked((short)stack));
            if (!VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, (INpcLootRollSource)random, out var state)) return false;
            Drops.Add(state); return true;
        }
    }
}
