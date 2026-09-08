using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaMechanicalBossLootTests
{
    [Theory]
    [InlineData(125, 2106, 549, 1368)]
    [InlineData(126, 2106, 549, 1369)]
    [InlineData(127, 2107, 547, 1367)]
    [InlineData(134, 2113, 548, 1366)]
    public void Classic_exact_source_order_and_material_stack_bounds(int type, int mask, int soul, int trophy)
    {
        var rolls = new ScriptedRolls("L7=0", "N1:2=1", "L1=0", "N15:31=30", "L1=0", "N25:41=40",
            "L10=0", "N1:2=1");
        var sink = new Sink();
        Assert.True(VanillaMechanicalBossLootEvaluator.TryExecute(new(new(type), false, false),
            new(100, 200), [], rolls, sink, out MechanicalBossLootExecutionResult result));
        Assert.Equal([mask, 1225, soul, trophy], sink.World.Select(static x => x.Drop.ItemType.Value));
        Assert.Equal([1, 30, 40, 1], sink.World.Select(static x => (int)x.Drop.Stack));
        Assert.Equal(new MechanicalBossLootExecutionResult(4, 0, 0, 0), result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(125, 3326)]
    [InlineData(126, 3326)]
    [InlineData(127, 3327)]
    [InlineData(134, 3325)]
    public void Expert_bag_is_raw_not_luck_and_classic_is_excluded(int type, int bag)
    {
        var rolls = new ScriptedRolls("N0:1=0", "N1:2=1", "L10=9");
        var sink = new Sink();
        Assert.True(VanillaMechanicalBossLootEvaluator.TryExecute(new(new(type), true, false),
            new(100, 200), [new(new(3), 10, 20)], rolls, sink, out var result));
        Assert.Equal(new NpcLootDrop(new(bag), 1), Assert.Single(sink.Bags));
        Assert.Empty(sink.World);
        Assert.Equal(new MechanicalBossLootExecutionResult(0, 1, 1, 0), result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(125, 4931, 4804, 1368)]
    [InlineData(126, 4931, 4804, 1369)]
    [InlineData(127, 4933, 4805, 1367)]
    [InlineData(134, 4932, 4803, 1366)]
    public void Master_relic_luck_then_per_player_raw_pet_and_trophy(int type, int relic, int pet, int trophy)
    {
        var rolls = new ScriptedRolls("N0:1=0", "N1:2=1", "L1=0", "N1:2=1", "N1:2=1",
            "N0:4=3", "N0:4=0", "L10=0", "N1:2=1");
        var sink = new Sink();
        Assert.True(VanillaMechanicalBossLootEvaluator.TryExecute(new(new(type), true, true),
            new(100, 200), [new(new(1), 10, 20), new(new(4), 30, 40)], rolls, sink, out var result));
        Assert.Equal([relic, pet, trophy], sink.World.Select(static x => x.Drop.ItemType.Value));
        Assert.Equal(new NpcLootWorldItemOrigin(30, 40), sink.World[1].Origin);
        Assert.Equal(new MechanicalBossLootExecutionResult(3, 1, 2, 1), result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(125, false, false, 1368)]
    [InlineData(126, false, false, 1369)]
    [InlineData(125, true, false, 1368)]
    [InlineData(126, true, false, 1369)]
    [InlineData(125, true, true, 1368)]
    [InlineData(126, true, true, 1369)]
    public void First_twin_can_drop_own_trophy_but_never_encounter_rewards(int type, bool expert, bool master, int trophy)
    {
        var rolls = new ScriptedRolls("L10=0", "N1:2=1");
        var sink = new Sink { OnlySupported = new(trophy) };
        Assert.True(VanillaMechanicalBossLootEvaluator.TryExecute(new(new(type), expert, master, OtherTwinActive: true),
            new(100, 200), [new(new(1), 10, 20)], rolls, sink, out var result));
        Assert.Equal(new ItemTypeId(trophy), Assert.Single(sink.World).Drop.ItemType);
        Assert.Empty(sink.Bags);
        Assert.Equal(new MechanicalBossLootExecutionResult(1, 0, 0, 0), result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(128, false, false, false)]
    [InlineData(135, false, false, false)]
    [InlineData(136, true, false, false)]
    [InlineData(127, false, true, false)]
    [InlineData(134, false, false, true)]
    public void Unsupported_child_or_invalid_context_fails_before_any_draw(int type, bool expert, bool master, bool other)
    {
        var rolls = new ScriptedRolls();
        var sink = new Sink();
        Assert.False(VanillaMechanicalBossLootEvaluator.TryExecute(new(new(type), expert, master, other),
            new(100, 200), [], rolls, sink, out _));
        Assert.Empty(sink.World);
        Assert.Empty(sink.Bags);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(547, 18, 18, true)]
    [InlineData(548, 18, 18, true)]
    [InlineData(549, 18, 18, true)]
    [InlineData(1225, 20, 20, false)]
    [InlineData(1366, 30, 30, false)]
    [InlineData(1367, 30, 30, false)]
    [InlineData(1368, 30, 30, false)]
    [InlineData(1369, 30, 30, false)]
    [InlineData(2106, 28, 20, false)]
    [InlineData(2107, 28, 20, false)]
    [InlineData(2113, 28, 20, false)]
    [InlineData(3325, 24, 24, false)]
    [InlineData(3326, 24, 24, false)]
    [InlineData(3327, 24, 24, false)]
    [InlineData(4803, 16, 30, false)]
    [InlineData(4804, 16, 30, false)]
    [InlineData(4805, 16, 30, false)]
    [InlineData(4931, 14, 14, false)]
    [InlineData(4932, 14, 14, false)]
    [InlineData(4933, 14, 14, false)]
    public void Item_defaults_and_no_gravity_are_source_pinned_without_use_admission(int id, int width, int height, bool noGravity)
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(new(id), out var item));
        Assert.Equal(new VanillaItemRuntimeDefaults(width, height, 9999), item.RuntimeDefaults);
        Assert.Equal(new VanillaItemWorldDropDefinition(width, height, noGravity, VanillaItemPrefixFamily.None), item.WorldDrop);
        Assert.Null(item.UseTiming);
        Assert.Null(item.Placement);
        Assert.Null(item.PickTool);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(new(id)));
    }

    [Theory]
    [InlineData(547)]
    [InlineData(548)]
    [InlineData(549)]
    public void Soul_materialization_uses_no_gravity_velocity_rng(int type)
    {
        var rolls = new ScriptedRolls("N-30:31=12", "N-30:31=30");
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(new(100, 200),
            new(new(type), 40), rolls, out var item));
        Assert.Equal((91f, 191f, 1.2f, 3f), (item.PositionX, item.PositionY, item.VelocityX, item.VelocityY));
        Assert.Equal((byte)0, item.Prefix);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Missing_reward_capability_and_unordered_interactors_do_not_partially_deliver()
    {
        var rolls = new ScriptedRolls();
        var sink = new Sink { OnlySupported = new(1368) };
        Assert.False(VanillaMechanicalBossLootEvaluator.TryExecute(new(new(125), false, false),
            new(100, 200), [], rolls, sink, out _));
        var supported = new Sink();
        Assert.False(VanillaMechanicalBossLootEvaluator.TryExecute(new(new(125), true, true),
            new(100, 200), [new(new(4), 10, 20), new(new(1), 30, 40)], rolls, supported, out _));
        Assert.Empty(sink.World);
        Assert.Empty(supported.World);
        Assert.Empty(supported.Bags);
        rolls.AssertConsumed();
    }

    private sealed class ScriptedRolls(params string[] expected) : INpcLootRollSource
    {
        private readonly Queue<string> calls = new(expected);
        public int RollLuck(int denominator) => Take($"L{denominator}");
        public int NextInt32(int min, int max) => Take($"N{min}:{max}");
        private int Take(string actual)
        {
            Assert.NotEmpty(calls);
            string[] call = calls.Dequeue().Split('=');
            Assert.Equal(call[0], actual);
            return int.Parse(call[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        public void AssertConsumed() => Assert.Empty(calls);
    }

    private sealed class Sink : IMechanicalBossLootDeliverySink
    {
        public ItemTypeId OnlySupported { get; init; }
        public List<(NpcLootDrop Drop, NpcLootWorldItemOrigin Origin)> World { get; } = [];
        public List<NpcLootDrop> Bags { get; } = [];
        public bool CanDeliverInstanced(ItemTypeId itemType) => CanDeliverWorldItem(itemType);
        public bool CanDeliverWorldItem(ItemTypeId itemType) =>
            (OnlySupported == default || itemType == OnlySupported) &&
            VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(itemType);
        public bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
            ReadOnlySpan<VanillaMechanicalBossLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random)
        {
            Assert.Equal(54_000, slotLeaseTicks);
            Bags.Add(drop);
            return true;
        }
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        {
            World.Add((drop, origin));
            return true;
        }
    }
}
