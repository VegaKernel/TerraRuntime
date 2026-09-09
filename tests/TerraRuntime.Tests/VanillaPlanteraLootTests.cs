using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaPlanteraLootTests
{
    [Fact]
    public void First_classic_kill_resolves_launcher_and_rockets_before_mask_key_and_optional_rewards()
    {
        var rolls = new ScriptedRolls("L10=0", "N1:2=1", "L1=0", "N1:2=1", "L1=0", "N50:151=150",
            "L7=0", "N1:2=1", "L1=0", "N1:2=1", "L20=0", "N1:2=1",
            "L50=0", "N1:2=1", "L4=0", "N1:2=1", "L10=0", "N1:2=1");
        var sink = new RecordingSink();
        Assert.True(VanillaPlanteraLootEvaluator.TryExecute(new(false, false, false), new(100, 200),
            [], rolls, sink, out var result));
        Assert.Equal([1370,758,771,2109,1141,1182,1305,1157,3021], sink.World.Select(x => x.Drop.ItemType.Value));
        Assert.Equal(150, sink.World[2].Drop.Stack);
        Assert.Equal(new PlanteraLootExecutionResult(9,0,0,0), result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(0, 758)]
    [InlineData(1, 1255)]
    [InlineData(2, 788)]
    [InlineData(3, 1178)]
    [InlineData(4, 1259)]
    [InlineData(5, 1155)]
    [InlineData(6, 3018)]
    [InlineData(7, 5477)]
    public void Repeat_classic_kill_selects_all_eight_rules_using_raw_rng_then_common_luck(int option, int weapon)
    {
        var calls = new List<string> { "L10=9", "N0:1=0", $"N0:8={option}", "L1=0", "N1:2=1" };
        if (option == 0) calls.AddRange(["L1=0", "N50:151=50"]);
        calls.AddRange(["L7=6", "L1=0", "N1:2=1", "L20=19", "L50=49", "L4=3", "L10=9"]);
        var rolls = new ScriptedRolls(calls.ToArray());
        var sink = new RecordingSink();
        Assert.True(VanillaPlanteraLootEvaluator.TryExecute(new(false, false, true), new(100,200),
            [], rolls, sink, out _));
        Assert.Equal(option == 0 ? [weapon,771,1141] : new[] {weapon,1141},
            sink.World.Select(x => x.Drop.ItemType.Value));
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expert_bag_replaces_classic_rewards_even_on_first_kill(bool downed)
    {
        var rolls = new ScriptedRolls("L10=9", "N0:1=0", "N1:2=1");
        var sink = new RecordingSink();
        VanillaPlanteraLootPlayer[] players = [new(new(1),10,20),new(new(4),30,40)];
        Assert.True(VanillaPlanteraLootEvaluator.TryExecute(new(true,false,downed),new(100,200),
            players,rolls,sink,out var result));
        Assert.Equal(new NpcLootDrop(new(3328),1),Assert.Single(sink.Instanced));
        Assert.Equal(players,sink.Recipients);
        Assert.Empty(sink.World);
        Assert.Equal(new PlanteraLootExecutionResult(0,1,2,0),result);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Master_relic_and_per_interactor_pet_keep_source_rng_order()
    {
        var rolls = new ScriptedRolls("L10=0","N1:2=1","N0:1=0","N1:2=1","L1=0","N1:2=1",
            "N1:2=1","N0:4=3","N0:4=0");
        var sink = new RecordingSink();
        Assert.True(VanillaPlanteraLootEvaluator.TryExecute(new(true,true,false),new(100,200),
            [new(new(1),10,20),new(new(4),30,40)],rolls,sink,out var result));
        Assert.Equal([1370,4934,4806],sink.World.Select(x=>x.Drop.ItemType.Value));
        Assert.Equal(new NpcLootWorldItemOrigin(30,40),sink.World[2].Origin);
        Assert.Equal(new PlanteraLootExecutionResult(3,1,2,1),result);
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
        var sink = new RecordingSink { Unsupported = invalid == 5 ? new ItemTypeId(1141) : default };
        var context = new VanillaPlanteraLootContext(false,invalid == 0,false);
        var origin = new NpcLootWorldItemOrigin(invalid == 1 ? float.NaN : 100,200);
        VanillaPlanteraLootPlayer[] players = invalid switch {
            2 => [new(new(4),10,20),new(new(1),30,40)],
            3 => [new(new(1),10,20),new(new(1),30,40)],
            4 => [new(new(255),10,20)], _ => []
        };
        Assert.False(VanillaPlanteraLootEvaluator.TryExecute(context,origin,players,rolls,sink,out _));
        Assert.Empty(sink.World);
        Assert.Empty(sink.Instanced);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(3328,24,24)]
    [InlineData(4934,14,14)]
    [InlineData(4806,16,30)]
    [InlineData(2109,28,20)]
    [InlineData(1141,14,20)]
    [InlineData(1182,16,30)]
    [InlineData(1305,24,28)]
    [InlineData(1157,26,28)]
    [InlineData(3021,18,28)]
    [InlineData(758,50,20)]
    [InlineData(771,20,14)]
    [InlineData(1255,24,22)]
    [InlineData(788,26,28)]
    [InlineData(1178,24,18)]
    [InlineData(1259,30,10)]
    [InlineData(1155,50,18)]
    [InlineData(3018,50,20)]
    [InlineData(5477,18,18)]
    [InlineData(1370,30,30)]
    public void Official_defaults_materialize_without_admitting_unverified_weapon_use(int id,int width,int height)
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(new(id),out var item));
        Assert.Equal(new VanillaItemRuntimeDefaults(width,height,9999),item.RuntimeDefaults);
        Assert.Null(item.UseTiming);
        Assert.Null(item.Placement);
        Assert.Null(item.PickTool);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(new(id)));
    }

    [Theory]
    [InlineData(1305, VanillaItemPrefixFamily.Sword, 40)]
    [InlineData(1157, VanillaItemPrefixFamily.Summon, 22)]
    [InlineData(758, VanillaItemPrefixFamily.Ranged, 35)]
    [InlineData(1255, VanillaItemPrefixFamily.Ranged, 35)]
    [InlineData(788, VanillaItemPrefixFamily.Magic, 36)]
    [InlineData(1178, VanillaItemPrefixFamily.Magic, 36)]
    [InlineData(1259, VanillaItemPrefixFamily.Spear, 14)]
    [InlineData(1155, VanillaItemPrefixFamily.Magic, 36)]
    [InlineData(3018, VanillaItemPrefixFamily.Sword, 40)]
    [InlineData(5477, VanillaItemPrefixFamily.Sword, 40)]
    public void Official_prefix_families_and_rounding_guards_are_exact(int id,VanillaItemPrefixFamily family,int count)
    {
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(new(id),out var item));
        Assert.Equal(family,item.PrefixFamily);
        var prefixes = VanillaItemPrefixCatalog.GetRollablePrefixes(family);
        Assert.Equal(count,prefixes.Length);
        foreach (var prefix in prefixes)
            Assert.Equal(!(id == 1255 && prefix.Value is 20 or 45),
                VanillaItemPrefixCatalog.IsValidForItem(new(id),prefix));
        Assert.False(VanillaItemPrefixCatalog.IsValidForItem(new(id),new(98)));
    }

    [Fact]
    public void Venus_magnum_rerolls_deadly_and_nimble_then_materializes_valid_prefix_and_velocity()
    {
        var rolls = new ScriptedRolls("N0:4=1","N0:35=4",
            "N0:4=1","N0:35=24","N0:4=1","N0:35=0","N-30:31=12","N-40:-15=-20");
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(new(100,200),
            new(VanillaPlanteraItemIds.VenusMagnum,1),rolls,out var drop));
        Assert.Equal(16,drop.Prefix);
        Assert.Equal((88f,189f),(drop.PositionX,drop.PositionY));
        Assert.Equal((1.2f,-2f),(drop.VelocityX,drop.VelocityY));
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

    private sealed class RecordingSink : IPlanteraLootDeliverySink
    {
        public ItemTypeId Unsupported { get; init; }
        public List<(NpcLootDrop Drop, NpcLootWorldItemOrigin Origin)> World { get; } = [];
        public List<NpcLootDrop> Instanced { get; } = [];
        public VanillaPlanteraLootPlayer[] Recipients { get; private set; } = [];
        public bool CanDeliverInstanced(ItemTypeId itemType) => CanDeliverWorldItem(itemType);
        public bool CanDeliverWorldItem(ItemTypeId itemType) =>
            itemType != Unsupported && VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(itemType);
        public bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
            ReadOnlySpan<VanillaPlanteraLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random)
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
