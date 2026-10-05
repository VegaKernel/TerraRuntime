using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;

namespace TerraRuntime.Tests;

public sealed class NpcDerivedHealing1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(NpcDerivedHealing1458Tests).Assembly.GetManifestResourceStream("TownDerivedHeals1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_player_phase_and_authenticated_health_ingress_feed_derived_heart_eligibility(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        int life = row.GetProperty("life").GetInt32();
        Assert.Equal(life, row.GetProperty("baseMaximum").GetInt32());
        Assert.True(life < row.GetProperty("derived").GetInt32());
        // The source harness assigns the callback actor's type/body on a genuine fresh NPC.
        // Its retained netID, lifeMax and damage remain constructor zeroes, not SetDefaults(type).
        var context = new VanillaNpcHealingContext1458(new(row.GetProperty("type").GetInt32()),
            default, 0, 0, NeedsLife: true, NeedsMana: false, ExpertMode: false);
        var random = new Rolls(row.GetProperty("seed").GetInt32()); var sink = new Sink();
        var origin = new NpcLootWorldItemOrigin(16000, 1600);
        Assert.True(VanillaNpcHealingLoot1458.TryExecute(in context, in origin, random, sink));
        Assert.Equal(row.GetProperty("hearts").GetInt32(), sink.Items.Count);
        var frames = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!))
            .Where(x => x[2] == 21).ToArray();
        Assert.Equal(frames.Length, sink.Items.Count);
        for (int i = 0; i < frames.Length; i++)
        {
            byte[] frame = frames[i]; var item = sink.Items[i];
            Assert.Equal((BitConverter.ToSingle(frame, 5), BitConverter.ToSingle(frame, 9),
                BitConverter.ToSingle(frame, 13), BitConverter.ToSingle(frame, 17)),
                (item.PositionX, item.PositionY, item.VelocityX, item.VelocityY));
            Assert.Equal((BitConverter.ToInt16(frame, 21), frame[23], BitConverter.ToInt16(frame, 25)),
                (item.Stack, item.Prefix, item.ItemNetId));
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Random.Next());
    }

    private sealed class Rolls(int seed) : INpcLootRollSource
    {
        internal readonly VanillaUnifiedRandom1458 Random = new(seed);
        public int RollLuck(int denominator) => Random.Next(denominator);
        public int NextInt32(int minimum, int maximum) => Random.Next(minimum, maximum);
    }
    private sealed class Sink : IBossRecoveryLootDeliverySink1458
    {
        internal readonly List<WorldItemDropStateUpdate> Items = [];
        public bool CanDeliverWorldItem(ItemTypeId type) => true;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        {
            if (!VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, random, out var item)) return false;
            Items.Add(item); return true;
        }
    }
}
