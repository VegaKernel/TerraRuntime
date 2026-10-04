using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;

namespace TerraRuntime.Tests;

public sealed class NpcHealingLoot1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using var resource = typeof(NpcHealingLoot1458Tests).Assembly.GetManifestResourceStream("NpcHealingLoot1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    // Original NPC.NPCLoot_DropHeals: 39 types, seeds/difficulty/luck/vitals/combat and tenth-anniversary state.
    // SHA256 ee72838aa29bab1b63f2865abd11cccc8dace130cfe8979671851181a60b81ff.
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Healing_matches_original_physical_drops_and_next_rng(JsonElement row)
    {
        var random = new Rolls(row.GetProperty("seed").GetInt32(), row.GetProperty("luck").GetSingle());
        var materializer = new VanillaNpcLootWorldItemMaterializer(
            () => new(false, false, row.GetProperty("mode").GetInt32() == 4));
        int vitals = row.GetProperty("vitals").GetInt32();
        var context = new VanillaNpcHealingContext1458(
            new(row.GetProperty("type").GetInt32()), new(row.GetProperty("netId").GetInt32()),
            row.GetProperty("lifeMax").GetInt32(), row.GetProperty("damage").GetInt32(),
            (vitals & 1) != 0, (vitals & 2) != 0, row.GetProperty("difficulty").GetInt32() == 1);
        var origin = new NpcLootWorldItemOrigin(1000 + row.GetProperty("width").GetInt32() / 2,
            1000 + row.GetProperty("height").GetInt32() / 2);
        var sink = new Sink(materializer);
        Assert.True(VanillaNpcHealingLoot1458.TryExecute(in context, in origin, random, sink));

        var expected = row.GetProperty("drops");
        Assert.Equal(expected.GetArrayLength(), sink.Drops.Count);
        Assert.InRange(sink.Drops.Count, 0, VanillaNpcHealingLoot1458.MaximumHealingDrops);
        for (int index = 0; index < sink.Drops.Count; index++)
        {
            var source = expected[index];
            var actual = sink.Drops[index];
            Assert.Equal((source.GetProperty("id").GetInt32(), source.GetProperty("stack").GetInt32(),
                source.GetProperty("prefix").GetInt32()), ((int)actual.ItemNetId, actual.Stack, (int)actual.Prefix));
            Assert.Equal((source.GetProperty("x").GetSingle(), source.GetProperty("y").GetSingle(),
                source.GetProperty("vx").GetSingle(), source.GetProperty("vy").GetSingle()),
                (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Source.Next());
    }

    private sealed class Sink(VanillaNpcLootWorldItemMaterializer materializer) : IBossRecoveryLootDeliverySink1458
    {
        public List<WorldItemDropStateUpdate> Drops { get; } = [];
        public bool CanDeliverWorldItem(ItemTypeId type) => true;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        {
            if (!materializer.TryMaterialize(in origin, in drop, random, out var state)) return false;
            Drops.Add(state);
            return true;
        }
    }

    private sealed class Rolls(int seed, float luck) : INpcLootRollSource
    {
        public VanillaUnifiedRandom1458 Source { get; } = new(seed);
        public int NextInt32(int min, int max) => Source.Next(min, max);
        public int RollLuck(int denominator)
        {
            if (luck > 0 && (float)Source.NextDouble() < luck)
                return Source.Next(Source.Next(denominator / 2, denominator));
            if (luck < 0 && (float)Source.NextDouble() < -luck)
                return Source.Next(Source.Next(denominator, denominator * 2));
            return Source.Next(denominator);
        }
    }
}
