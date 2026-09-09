using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaGolemLootTests
{
    [Theory]
    [InlineData(0,1258)]
    [InlineData(1,1122)]
    [InlineData(2,899)]
    [InlineData(3,1248)]
    [InlineData(4,1295)]
    [InlineData(5,1296)]
    [InlineData(6,1297)]
    public void Classic_preserves_mask_picksaw_mobius_then_nested_reward_and_husks_order(int option,int reward)
    {
        var calls = new List<string> { "L10=0","N1:2=1","L7=0","N1:2=1","L4=0","N1:2=1","L6=0","N1:2=1",
            "N0:1=0",$"N0:7={option}","L1=0","N1:2=1" };
        if (option == 0) calls.AddRange(["L1=0","N60:181=180"]);
        calls.AddRange(["L1=0","N4:9=8"]);
        var rolls = new ScriptedRolls(calls.ToArray());
        var sink = new RecordingSink();
        Assert.True(VanillaGolemLootEvaluator.TryExecute(new(false,false),new(100,200),[],rolls,sink,out _));
        Assert.Equal(option == 0 ? [1371,2110,1294,6158,reward,1261,2218] : new[]{1371,2110,1294,6158,reward,2218},
            sink.World.Select(x=>x.Drop.ItemType.Value));
        Assert.Equal(8,sink.World.Single(x=>x.Drop.ItemType.Value==2218).Drop.Stack);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Classic_optional_rolls_can_fail_without_losing_selected_reward_or_husks()
    {
        var rolls = new ScriptedRolls("L10=9","L7=6","L4=3","L6=5","N0:1=0","N0:7=2","L1=0","N1:2=1",
            "L1=0","N4:9=4");
        var sink = new RecordingSink();
        Assert.True(VanillaGolemLootEvaluator.TryExecute(new(false,false),new(100,200),[],rolls,sink,out _));
        Assert.Equal([899,2218],sink.World.Select(x=>x.Drop.ItemType.Value));
        rolls.AssertConsumed();
    }

    [Fact]
    public void Expert_bag_replaces_classic_rewards()
    {
        var rolls = new ScriptedRolls("L10=9", "N0:1=0", "N1:2=1");
        var sink = new RecordingSink();
        VanillaGolemLootPlayer[] players = [new(new(1),10,20),new(new(4),30,40)];
        Assert.True(VanillaGolemLootEvaluator.TryExecute(new(true,false),new(100,200),
            players,rolls,sink,out var result));
        Assert.Equal(new NpcLootDrop(new(3329),1),Assert.Single(sink.Instanced));
        Assert.Equal(players,sink.Recipients);
        Assert.Empty(sink.World);
        Assert.Equal(new GolemLootExecutionResult(0,1,2,0),result);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Master_relic_and_per_interactor_pet_keep_source_rng_order()
    {
        var rolls = new ScriptedRolls("L10=0","N1:2=1","N0:1=0","N1:2=1","L1=0","N1:2=1",
            "N1:2=1","N0:4=3","N0:4=0");
        var sink = new RecordingSink();
        Assert.True(VanillaGolemLootEvaluator.TryExecute(new(true,true),new(100,200),
            [new(new(1),10,20),new(new(4),30,40)],rolls,sink,out var result));
        Assert.Equal([1371,4935,4807],sink.World.Select(x=>x.Drop.ItemType.Value));
        Assert.Equal(new NpcLootWorldItemOrigin(30,40),sink.World[2].Origin);
        Assert.Equal(new GolemLootExecutionResult(3,1,2,1),result);
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
        var sink = new RecordingSink { Unsupported = invalid == 5 ? new ItemTypeId(2218) : default };
        var context = new VanillaGolemLootContext(false,invalid == 0);
        var origin = new NpcLootWorldItemOrigin(invalid == 1 ? float.NaN : 100,200);
        VanillaGolemLootPlayer[] players = invalid switch {
            2 => [new(new(4),10,20),new(new(1),30,40)],
            3 => [new(new(1),10,20),new(new(1),30,40)],
            4 => [new(new(255),10,20)], _ => []
        };
        Assert.False(VanillaGolemLootEvaluator.TryExecute(context,origin,players,rolls,sink,out _));
        Assert.Empty(sink.World);
        Assert.Empty(sink.Instanced);
        rolls.AssertConsumed();
    }


    [Theory]
    [InlineData(3329,24,24)]
    [InlineData(4935,14,14)]
    [InlineData(4807,16,30)]
    [InlineData(2110,28,20)]
    [InlineData(1294,20,12)]
    [InlineData(6158,24,24)]
    [InlineData(1258,50,18)]
    [InlineData(1261,10,28)]
    [InlineData(1122,18,20)]
    [InlineData(899,16,24)]
    [InlineData(1248,24,24)]
    [InlineData(1295,24,18)]
    [InlineData(1296,26,28)]
    [InlineData(1297,30,10)]
    [InlineData(2218,14,18)]
    [InlineData(1371,30,30)]
    public void Official_defaults_materialize_without_admitting_unverified_item_use(int id,int width,int height)
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(new(id),out var item));
        Assert.Equal(new VanillaItemRuntimeDefaults(width,height,9999),item.RuntimeDefaults);
        Assert.Null(item.UseTiming);
        Assert.Null(item.Placement);
        Assert.Null(item.PickTool);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(new(id)));
    }

    [Theory]
    [InlineData(1294, VanillaItemPrefixFamily.Sword, 40)]
    [InlineData(6158, VanillaItemPrefixFamily.Accessory, 19)]
    [InlineData(1258, VanillaItemPrefixFamily.Ranged, 35)]
    [InlineData(1122, VanillaItemPrefixFamily.Spear, 14)]
    [InlineData(899, VanillaItemPrefixFamily.Accessory, 19)]
    [InlineData(1248, VanillaItemPrefixFamily.Accessory, 19)]
    [InlineData(1295, VanillaItemPrefixFamily.Magic, 36)]
    [InlineData(1296, VanillaItemPrefixFamily.Magic, 36)]
    [InlineData(1297, VanillaItemPrefixFamily.Spear, 14)]
    public void Official_prefix_families_and_rounding_guards_are_exact(int id,VanillaItemPrefixFamily family,int count)
    {
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(new(id),out var item));
        Assert.Equal(family,item.PrefixFamily);
        var prefixes = VanillaItemPrefixCatalog.GetRollablePrefixes(family);
        Assert.Equal(count,prefixes.Length);
        foreach (var prefix in prefixes)
            Assert.Equal(!(id == 1295 && prefix.Value == 45),VanillaItemPrefixCatalog.IsValidForItem(new(id),prefix));
        Assert.False(VanillaItemPrefixCatalog.IsValidForItem(new(id),new(98)));
    }

    [Fact]
    public void Heat_ray_rerolls_nimble_before_accepting_mystic()
    {
        var rolls = new ScriptedRolls("N0:4=1","N0:36=25","N0:4=1","N0:36=0");
        Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(VanillaGolemItemIds.HeatRay,rolls,out var prefix));
        Assert.Equal(26,prefix.Value);
        rolls.AssertConsumed();
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

    private sealed class RecordingSink : IGolemLootDeliverySink
    {
        public ItemTypeId Unsupported { get; init; }
        public List<(NpcLootDrop Drop, NpcLootWorldItemOrigin Origin)> World { get; } = [];
        public List<NpcLootDrop> Instanced { get; } = [];
        public VanillaGolemLootPlayer[] Recipients { get; private set; } = [];
        public bool CanDeliverInstanced(ItemTypeId itemType) => CanDeliverWorldItem(itemType);
        public bool CanDeliverWorldItem(ItemTypeId itemType) =>
            itemType != Unsupported && VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(itemType);
        public bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
            ReadOnlySpan<VanillaGolemLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random)
        {
            Assert.Equal(54_000, slotLeaseTicks);
            Instanced.Add(drop);
            Recipients = recipients.ToArray();
            return true;
        }
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        {
            World.Add((drop, origin));
            return true;
        }
    }
}
