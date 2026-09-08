using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaEyeOfCthulhuLootTests
{
    [Theory]
    [InlineData(false, 20, 30, 1)]
    [InlineData(false, 50, 90, 3)]
    [InlineData(true, 20, 30, 1)]
    [InlineData(true, 50, 90, 3)]
    public void Classic_source_order_pins_ore_seed_branch_and_inclusive_stack_bounds(bool crimson, int arrows, int ore, int seeds)
    {
        var rolls = new ScriptedRolls("L7=0", "N1:2=1", "L40=0", "N1:2=1",
            "L1=0", $"N20:51={arrows}", "L1=0", $"N30:91={ore}", "L1=0", $"N1:4={seeds}", "L10=0", "N1:2=1");
        var sink = new RecordingSink();
        Assert.True(VanillaEyeOfCthulhuLootEvaluator.TryExecute(new(false, false, crimson),
            new(100, 200), [], rolls, sink, out var result));
        Assert.Equal([2112, 1299, 47, crimson ? 880 : 56, crimson ? 2171 : 59, 1360],
            sink.World.Select(static x => x.Drop.ItemType.Value));
        Assert.Equal([1, 1, arrows, ore, seeds, 1], sink.World.Select(static x => (int)x.Drop.Stack));
        Assert.Equal(new EyeOfCthulhuLootExecutionResult(6, 0, 0, 0), result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classic_failed_optional_rolls_do_not_consume_stacks_or_require_opposite_evil_capability(bool crimson)
    {
        var rolls = new ScriptedRolls("L7=6", "L40=39", "L1=0", "N20:51=20",
            "L1=0", "N30:91=30", "L1=0", "N1:4=1", "L10=9");
        var sink = new RecordingSink { Unsupported = new(crimson ? 56 : 880) };
        Assert.True(VanillaEyeOfCthulhuLootEvaluator.TryExecute(new(false, false, crimson),
            new(100, 200), [], rolls, sink, out var result));
        Assert.Equal(3, result.WorldItemCount);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expert_bag_does_not_use_classic_evil_branch(bool crimson)
    {
        var rolls = new ScriptedRolls("N0:1=0", "N1:2=1", "L10=9");
        var sink = new RecordingSink();
        Assert.True(VanillaEyeOfCthulhuLootEvaluator.TryExecute(new(true, false, crimson),
            new(100, 200), [new(new(2), 10, 20)], rolls, sink, out var result));
        Assert.Equal(new NpcLootDrop(new(3319), 1), Assert.Single(sink.Instanced));
        Assert.Empty(sink.World);
        Assert.Equal(new EyeOfCthulhuLootExecutionResult(0, 1, 1, 0), result);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Master_has_two_guaranteed_common_drops_before_independent_per_player_pet()
    {
        var rolls = new ScriptedRolls("N0:1=0", "N1:2=1", "L1=0", "N1:2=1",
            "L1=0", "N1:2=1", "N1:2=1", "N0:4=3", "N0:4=0", "L10=0", "N1:2=1");
        var sink = new RecordingSink();
        Assert.True(VanillaEyeOfCthulhuLootEvaluator.TryExecute(new(true, true, false), new(100, 200),
            [new(new(1), 10, 20), new(new(4), 30, 40)], rolls, sink, out var result));
        Assert.Equal([4924, 3763, 4798, 1360], sink.World.Select(static x => x.Drop.ItemType.Value));
        Assert.Equal(new NpcLootWorldItemOrigin(30, 40), sink.World[2].Origin);
        Assert.Equal(new EyeOfCthulhuLootExecutionResult(4, 1, 2, 1), result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(47, 10, 28)]
    [InlineData(59, 14, 14)]
    [InlineData(1299, 14, 28)]
    [InlineData(1360, 30, 30)]
    [InlineData(2112, 28, 20)]
    [InlineData(2171, 14, 14)]
    [InlineData(3319, 24, 24)]
    [InlineData(3763, 18, 18)]
    [InlineData(4798, 16, 30)]
    [InlineData(4924, 14, 14)]
    public void World_drop_dimensions_and_prefix_absence_do_not_admit_item_use(int id, int width, int height)
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(new(id), out var item));
        Assert.Equal(new VanillaItemRuntimeDefaults(width, height, 9999), item.RuntimeDefaults);
        Assert.Equal(new VanillaItemWorldDropDefinition(width, height, false, VanillaItemPrefixFamily.None), item.WorldDrop);
        Assert.Null(item.UseTiming);
        Assert.Null(item.Placement);
        Assert.Null(item.PickTool);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(new(id)));
    }

    [Fact]
    public void Invalid_context_or_recipients_and_unsupported_selected_ore_fail_before_any_draw()
    {
        var rolls = new ScriptedRolls();
        var sink = new RecordingSink();
        Assert.False(VanillaEyeOfCthulhuLootEvaluator.TryExecute(new(false, true, false),
            new(100, 200), [], rolls, sink, out _));
        Assert.False(VanillaEyeOfCthulhuLootEvaluator.TryExecute(new(true, true, false),
            new(100, 200), [new(new(1), 10, 20), new(new(1), 30, 40)], rolls, sink, out _));
        Assert.False(VanillaEyeOfCthulhuLootEvaluator.TryExecute(new(false, false, true),
            new(100, 200), [], rolls, new RecordingSink { Unsupported = new(880) }, out _));
        Assert.Empty(sink.World);
        Assert.Empty(sink.Instanced);
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

    private sealed class RecordingSink : IEyeOfCthulhuLootDeliverySink
    {
        public ItemTypeId Unsupported { get; init; }
        public List<(NpcLootDrop Drop, NpcLootWorldItemOrigin Origin)> World { get; } = [];
        public List<NpcLootDrop> Instanced { get; } = [];
        public VanillaEyeOfCthulhuLootPlayer[] Recipients { get; private set; } = [];
        public bool CanDeliverInstanced(ItemTypeId itemType) => CanDeliverWorldItem(itemType);
        public bool CanDeliverWorldItem(ItemTypeId itemType) =>
            itemType != Unsupported && VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(itemType);
        public bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
            ReadOnlySpan<VanillaEyeOfCthulhuLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random)
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
