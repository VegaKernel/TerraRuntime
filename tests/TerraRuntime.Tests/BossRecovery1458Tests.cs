using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class BossRecovery1458Tests
{
    // Independent original 1.4.5.8 DoDeathEvents_DropBossPotionsAndHearts + Item.NewItem.
    // SHA256 4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    public static IEnumerable<object[]> OriginalRows() => Rows("BossRecovery1458").Select(r => new object[] { r });
    internal static IEnumerable<JsonElement> Rows(string name)
    {
        using var stream = typeof(BossRecovery1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        return json.RootElement.EnumerateArray().Select(r => r.Clone()).ToArray();
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Complete_original_recovery_and_seasonal_materialization_matches(JsonElement row)
    {
        var random = new RandomAdapter(row.GetProperty("seed").GetInt32());
        int mode = row.GetProperty("mode").GetInt32();
        var sink = new Sink(new((mode & 1) != 0, (mode & 2) != 0, (mode & 4) != 0));
        var daily = new VanillaBossRecoveryDailyState1458();
        int type = row.GetProperty("type").GetInt32();
        if (row.GetProperty("paired").GetBoolean())
        {
            if (type == 4) daily.Record(VanillaNpcIds.WallOfFlesh);
            if (type == 113) daily.Record(VanillaNpcIds.EyeOfCthulhu);
        }
        Assert.True(VanillaBossRecovery1458.TryExecute(new(type), new(1050, 1060), daily, random, sink));
        var expected = row.GetProperty("drops").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, sink.Items.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].GetProperty("id").GetInt32(), sink.Items[i].ItemNetId);
            Assert.Equal(expected[i].GetProperty("stack").GetInt32(), sink.Items[i].Stack);
            Assert.Equal((1042f, 1052f), (sink.Items[i].PositionX, sink.Items[i].PositionY));
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Source.Next());
        Assert.Equal(row.GetProperty("eoc").GetBoolean(), daily.EyeKilled);
        Assert.Equal(row.GetProperty("wof").GetBoolean(), daily.WallKilled);
    }


    public static IEnumerable<object[]> PrefixRows() => Rows("BossRewardPrefix1458").Select(r=>new object[]{r});
    [Theory]
    [MemberData(nameof(PrefixRows))]
    public void Every_original_reward_family_and_stat_guard_matches_without_admitting_weapon_use(JsonElement row)
    {
        var type=new ItemTypeId(row.GetProperty("id").GetInt32());
        Assert.True(VanillaDefinitionCatalog.TryGetRuntimeDefaults(type,out var defaults));
        Assert.Equal((row.GetProperty("width").GetInt32(),row.GetProperty("height").GetInt32()),(defaults.Width,defaults.Height));
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(type,out var drop));
        Assert.Equal(row.GetProperty("rollable").EnumerateArray().Select(p=>p.GetInt32()),
            VanillaItemPrefixCatalog.GetRollablePrefixes(drop.PrefixFamily).ToArray().Select(p=>p.Value));
        var valid=row.GetProperty("valid").EnumerateArray().Select(p=>p.GetInt32()).ToHashSet();
        foreach(var prefix in VanillaItemPrefixCatalog.GetRollablePrefixes(drop.PrefixFamily))
            Assert.Equal(valid.Contains(prefix.Value),VanillaItemPrefixCatalog.IsValidForItem(type,prefix));
        Assert.True(VanillaNaturalItemPrefixRoller.CanRoll(type));
    }


    public static IEnumerable<object[]> SeasonalPickupRows()=>Rows("SeasonalPickupMaterialization1458").Select(r=>new object[]{r});
    [Theory]
    [MemberData(nameof(SeasonalPickupRows))]
    public void Heart_and_star_substitutions_materialize_in_original_rng_order(JsonElement row)
    {
        int mode=row.GetProperty("mode").GetInt32();
        var random=new RandomAdapter(row.GetProperty("seed").GetInt32());
        var sink=new Sink(new((mode&1)!=0,(mode&2)!=0,(mode&4)!=0));
        Assert.True(sink.TryDeliverWorldItem(new(1050,1060),new(new(row.GetProperty("type").GetInt32()),1),random));
        var actual=Assert.Single(sink.Items);
        Assert.Equal(row.GetProperty("id").GetInt32(),actual.ItemNetId);
        Assert.Equal((row.GetProperty("x").GetSingle(),row.GetProperty("y").GetSingle(),row.GetProperty("vx").GetSingle(),row.GetProperty("vy").GetSingle()),(actual.PositionX,actual.PositionY,actual.VelocityX,actual.VelocityY));
        Assert.Equal(row.GetProperty("next").GetInt32(),random.Source.Next());
    }

    [Fact]
    public void Missing_materialization_support_rejects_before_rng_or_daily_flags()
    {
        var random = new RandomAdapter(1458);
        var daily = new VanillaBossRecoveryDailyState1458();
        daily.Record(VanillaNpcIds.EyeOfCthulhu);
        Assert.False(VanillaBossRecovery1458.TryExecute(VanillaNpcIds.WallOfFlesh, new(10, 20), daily, random, new RejectSink()));
        Assert.Equal(new VanillaUnifiedRandom1458(1458).Next(), random.Source.Next());
        Assert.True(daily.EyeKilled);
        Assert.False(daily.WallKilled);
    }

    [Fact]
    public void Daily_flags_pair_once_in_either_order_and_reset_without_persistence()
    {
        foreach (bool reversed in new[] { false, true })
        {
            var state = new VanillaBossRecoveryDailyState1458();
            var first = reversed ? VanillaNpcIds.WallOfFlesh : VanillaNpcIds.EyeOfCthulhu;
            var last = reversed ? VanillaNpcIds.EyeOfCthulhu : VanillaNpcIds.WallOfFlesh;
            Assert.False(state.Record(first));
            Assert.False(state.Record(first));
            Assert.True(state.Record(last));
            Assert.False(state.EyeKilled || state.WallKilled);
            Assert.False(state.Record(last));
            state.Reset();
            Assert.False(state.Record(first));
        }
    }

    internal sealed class RandomAdapter(int seed) : INpcLootRollSource
    {
        public VanillaUnifiedRandom1458 Source { get; } = new(seed);
        public int RollLuck(int denominator) => Source.Next(denominator);
        public int NextInt32(int min, int max) => Source.Next(min, max);
    }
    private sealed class Sink(VanillaSeasonalItemDropContext1458 context) : IBossRecoveryLootDeliverySink1458
    {
        private readonly VanillaNpcLootWorldItemMaterializer materializer = new(() => context);
        public List<WorldItemDropStateUpdate> Items { get; } = [];
        public bool CanDeliverWorldItem(ItemTypeId type) => materializer.CanMaterialize(type);
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        {
            if (!materializer.TryMaterialize(in origin, in drop, random, out var item)) return false;
            Items.Add(item);
            return true;
        }
    }
    private sealed class RejectSink : IBossRecoveryLootDeliverySink1458
    {
        public bool CanDeliverWorldItem(ItemTypeId type) => false;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random) => throw new InvalidOperationException();
    }
}
