using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaMoonLordLootTests
{
    private static readonly int[] RewardIds = [3063,3389,3065,1553,3930,3541,3570,3571,3569,5480];

    public static TheoryData<int, int> OrderedSelections
    {
        get
        {
            var data = new TheoryData<int, int>();
            for (int first = 0; first < 10; first++)
                for (int second = 0; second < 9; second++)
                    data.Add(first, second);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OrderedSelections))]
    public void Classic_delivers_two_distinct_rewards_in_source_order_with_delivery_between_draws(int first, int second)
    {
        var rolls = new ScriptedRolls("L10=9", "L7=6", "L10=9", "L1=0", "N1:2=1", "L1=0", "N70:91=90",
            $"N0:10={first}", "N100:101=100", $"N0:9={second}", "N100:101=100");
        var sink = new RecordingSink { ConsumeWeaponDeliveryRandom = true };
        Assert.True(VanillaMoonLordLootEvaluator.TryExecute(new(false,false),new(100,200),[],rolls,sink,out var result));
        int expectedSecond = RewardIds.Where((_, index) => index != first).ElementAt(second);
        Assert.Equal([3384,3460,RewardIds[first],expectedSecond], sink.World.Select(x => x.Drop.ItemType.Value));
        Assert.Equal(90, sink.World[1].Drop.Stack);
        Assert.Equal(new MoonLordLootExecutionResult(4,0,0,0), result);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Classic_optional_drops_and_minimum_luminite_preserve_rule_order()
    {
        var rolls = new ScriptedRolls("L10=0","N1:2=1","L7=0","N1:2=1","L10=0","N1:2=1",
            "L1=0","N1:2=1","L1=0","N70:91=70","N0:10=9","N0:9=0");
        var sink = new RecordingSink();
        Assert.True(VanillaMoonLordLootEvaluator.TryExecute(new(false,false),new(100,200),[],rolls,sink,out _));
        Assert.Equal([3595,3373,4469,3384,3460,5480,3063], sink.World.Select(x => x.Drop.ItemType.Value));
        Assert.Equal(70, sink.World[4].Drop.Stack);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Expert_bag_replaces_classic_rewards_for_each_interactor()
    {
        var rolls = new ScriptedRolls("L10=9", "N0:1=0", "N1:2=1");
        var sink = new RecordingSink();
        VanillaMoonLordLootPlayer[] players = [new(new(1),10,20),new(new(4),30,40)];
        Assert.True(VanillaMoonLordLootEvaluator.TryExecute(new(true,false),new(100,200),players,rolls,sink,out var result));
        Assert.Equal(new NpcLootDrop(new(3332),1),Assert.Single(sink.Instanced));
        Assert.Equal(players,sink.Recipients);
        Assert.Empty(sink.World);
        Assert.Equal(new MoonLordLootExecutionResult(0,1,2,0),result);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Master_relic_and_per_interactor_pet_keep_source_rng_order()
    {
        var rolls = new ScriptedRolls("L10=0","N1:2=1","N0:1=0","N1:2=1","L1=0","N1:2=1",
            "N1:2=1","N0:4=3","N0:4=0");
        var sink = new RecordingSink();
        Assert.True(VanillaMoonLordLootEvaluator.TryExecute(new(true,true),new(100,200),
            [new(new(1),10,20),new(new(4),30,40)],rolls,sink,out var result));
        Assert.Equal([3595,4938,4810],sink.World.Select(x=>x.Drop.ItemType.Value));
        Assert.Equal(new NpcLootWorldItemOrigin(30,40),sink.World[2].Origin);
        Assert.Equal(new MoonLordLootExecutionResult(3,1,2,1),result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Invalid_inputs_and_unsupported_drop_fail_before_rng_or_delivery(int invalid)
    {
        var rolls = new ScriptedRolls();
        var sink = new RecordingSink { Unsupported = invalid == 5 ? new ItemTypeId(3460) : default };
        var context = new VanillaMoonLordLootContext(false,invalid == 0);
        var origin = new NpcLootWorldItemOrigin(invalid == 1 ? float.NaN : 100,200);
        VanillaMoonLordLootPlayer[] players = invalid switch {
            2 => [new(new(4),10,20),new(new(1),30,40)],
            3 => [new(new(1),10,20),new(new(1),30,40)],
            4 => [new(new(255),10,20)], _ => []
        };
        Assert.False(VanillaMoonLordLootEvaluator.TryExecute(context,origin,players,rolls,sink,out _));
        Assert.Empty(sink.World);
        Assert.Empty(sink.Instanced);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(3595, "EA1E1BDE95BE5766E6193E4FA640971A12A372B8E6B03272751AEF3D892F1050")]
    [InlineData(3332, "EA1E1BDE95BE5766E6193E4FA640971A12A372B8E6B03272751AEF3D892F1050")]
    [InlineData(4938, "EA1E1BDE95BE5766E6193E4FA640971A12A372B8E6B03272751AEF3D892F1050")]
    [InlineData(4810, "EA1E1BDE95BE5766E6193E4FA640971A12A372B8E6B03272751AEF3D892F1050")]
    [InlineData(3373, "EA1E1BDE95BE5766E6193E4FA640971A12A372B8E6B03272751AEF3D892F1050")]
    [InlineData(4469, "EA1E1BDE95BE5766E6193E4FA640971A12A372B8E6B03272751AEF3D892F1050")]
    [InlineData(3384, "EA1E1BDE95BE5766E6193E4FA640971A12A372B8E6B03272751AEF3D892F1050")]
    [InlineData(3460, "EA1E1BDE95BE5766E6193E4FA640971A12A372B8E6B03272751AEF3D892F1050")]
    [InlineData(3063, "1480A2DE2FC8DB619E4115B620B7F9D9A32F4E69C57FD2214DA1BADC8B426F48")]
    [InlineData(3389, "10E59806D86BD4766D330AA3E66140BA76BEBA2EF5F19772C5D5CD25D9E0E42D")]
    [InlineData(3065, "1480A2DE2FC8DB619E4115B620B7F9D9A32F4E69C57FD2214DA1BADC8B426F48")]
    [InlineData(1553, "04B7735B151DC272CC660EB0A19F0AC885490D85588C1085C3DBEE1CC57D8B74")]
    [InlineData(3930, "04B7735B151DC272CC660EB0A19F0AC885490D85588C1085C3DBEE1CC57D8B74")]
    [InlineData(3541, "D3980D32C8CBA18BD3E77CDF55C63E96211F3A6558420530A5B38FEB374EADF7")]
    [InlineData(3570, "D3980D32C8CBA18BD3E77CDF55C63E96211F3A6558420530A5B38FEB374EADF7")]
    [InlineData(3571, "0AB98B6DE9288777937BFD627E61EF57A58E752F016C62F2E105065A8B6EFA42")]
    [InlineData(3569, "0AB98B6DE9288777937BFD627E61EF57A58E752F016C62F2E105065A8B6EFA42")]
    [InlineData(5480, "1480A2DE2FC8DB619E4115B620B7F9D9A32F4E69C57FD2214DA1BADC8B426F48")]
    public void Original_server_prefix_and_rng_goldens_match_for_100_seeds(int id, string expectedHash)
    {
        // Captured from the unmodified official 1.4.5.8 Linux assembly via Item.SetDefaults/Prefix(-1).
        // Each record is little-endian int32 prefix followed by the next UnifiedRandom.Next() value.
        var bytes = new byte[100 * 8];
        for (int seed = 0; seed < 100; seed++)
        {
            var random = new Random(seed);
            Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(new(id),new SeededRolls(random),out var prefix));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(seed * 8),prefix.Value);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(seed * 8 + 4),random.Next());
        }
        Assert.Equal(expectedHash,Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Theory]
    [InlineData(3595, 30, 30)]
    [InlineData(3332, 24, 24)]
    [InlineData(4938, 14, 14)]
    [InlineData(4810, 16, 30)]
    [InlineData(3373, 28, 20)]
    [InlineData(4469, 36, 26)]
    [InlineData(3384, 16, 16)]
    [InlineData(3460, 12, 12)]
    [InlineData(3063, 30, 30)]
    [InlineData(3389, 24, 24)]
    [InlineData(3065, 30, 30)]
    [InlineData(1553, 60, 26)]
    [InlineData(3930, 20, 12)]
    [InlineData(3541, 16, 16)]
    [InlineData(3570, 40, 40)]
    [InlineData(3571, 18, 20)]
    [InlineData(3569, 18, 20)]
    [InlineData(5480, 18, 18)]
    public void Original_item_defaults_materialize_without_claiming_weapon_use(int id,int width,int height)
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(new(id),out var item));
        Assert.Equal(new VanillaItemRuntimeDefaults(width,height,9999),item.RuntimeDefaults);
        Assert.Null(item.UseTiming);
        Assert.Null(item.Placement);
        Assert.Null(item.PickTool);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(new(id)));
    }

    private sealed class SeededRolls(Random random) : INpcLootRollSource
    {
        public int NextInt32(int min, int max) => random.Next(min,max);
        public int RollLuck(int denominator) => random.Next(denominator);
    }

    private sealed class ScriptedRolls(params string[] expected) : INpcLootRollSource
    {
        private readonly Queue<string> calls = new(expected);
        public int RollLuck(int chanceDenominator) => Take($"L{chanceDenominator}");
        public int NextInt32(int inclusiveMin, int exclusiveMax) => Take($"N{inclusiveMin}:{exclusiveMax}");
        private int Take(string actual)
        {
            Assert.NotEmpty(calls);
            string[] call = calls.Dequeue().Split('=');
            Assert.Equal(call[0], actual);
            return int.Parse(call[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        public void AssertConsumed() => Assert.Empty(calls);
    }

    private sealed class RecordingSink : IMoonLordLootDeliverySink
    {
        public bool ConsumeWeaponDeliveryRandom { get; init; }
        public ItemTypeId Unsupported { get; init; }
        public List<(NpcLootDrop Drop, NpcLootWorldItemOrigin Origin)> World { get; } = [];
        public List<NpcLootDrop> Instanced { get; } = [];
        public VanillaMoonLordLootPlayer[] Recipients { get; private set; } = [];
        public bool CanDeliverInstanced(ItemTypeId itemType) => CanDeliverWorldItem(itemType);
        public bool CanDeliverWorldItem(ItemTypeId itemType) =>
            itemType != Unsupported && VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(itemType);
        public bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
            ReadOnlySpan<VanillaMoonLordLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random)
        {
            Assert.Equal(54_000, slotLeaseTicks);
            Instanced.Add(drop);
            Recipients = recipients.ToArray();
            return true;
        }
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        {
            World.Add((drop, origin));
            if (ConsumeWeaponDeliveryRandom && RewardIds.Contains(drop.ItemType.Value))
                Assert.Equal(100, random.NextInt32(100, 101));
            return true;
        }
    }
}
