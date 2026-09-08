using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaQueenSlimeLootTests
{
    [Theory]
    [InlineData(0, 4982)]
    [InlineData(1, 4983)]
    [InlineData(2, 4984)]
    public void Classic_preserves_exact_luck_raw_stack_and_armor_selection_order(int option, int armor)
    {
        var rolls = new ScriptedRolls(
            "L1=0", "N25:76=75", "L7=0", "N1:2=1", "L1=0", $"N0:3={option}",
            "L4=0", "N1:2=1", "L4=0", "N1:2=1", "N0:3=0", "N1:2=1", "L10=0", "N1:2=1");
        var sink = new RecordingSink();
        Assert.True(VanillaQueenSlimeLootEvaluator.TryExecute(new(false, false), new(100, 200),
            [], rolls, sink, out QueenSlimeLootExecutionResult result));
        Assert.Equal([4986, 4959, armor, 4758, 4981, 4980, 4958],
            sink.World.Select(static entry => entry.Drop.ItemType.Value));
        Assert.Equal([75, 1, 1, 1, 1, 1, 1], sink.World.Select(static entry => (int)entry.Drop.Stack));
        Assert.Equal(new QueenSlimeLootExecutionResult(7, 0, 0, 0), result);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Classic_hook_raw_roll_is_independent_of_failed_luck_rolls()
    {
        var rolls = new ScriptedRolls(
            "L1=0", "N25:76=25", "L7=6", "L1=0", "N0:3=1",
            "L4=3", "L4=3", "N0:3=0", "N1:2=1", "L10=9");
        var sink = new RecordingSink();
        Assert.True(VanillaQueenSlimeLootEvaluator.TryExecute(new(false, false), new(100, 200),
            [], rolls, sink, out _));
        Assert.Equal([4986, 4983, 4980], sink.World.Select(static entry => entry.Drop.ItemType.Value));
        rolls.AssertConsumed();
    }

    [Fact]
    public void Expert_delivers_only_interactor_bags_and_optional_trophy()
    {
        var rolls = new ScriptedRolls("N0:1=0", "N1:2=1", "L10=9");
        var sink = new RecordingSink();
        VanillaQueenSlimeLootPlayer[] players = [new(new(1), 10, 20), new(new(4), 30, 40)];
        Assert.True(VanillaQueenSlimeLootEvaluator.TryExecute(new(true, false), new(100, 200),
            players, rolls, sink, out QueenSlimeLootExecutionResult result));
        Assert.Equal(new NpcLootDrop(new(4957), 1), Assert.Single(sink.Instanced));
        Assert.Equal(players, sink.Recipients);
        Assert.Empty(sink.World);
        Assert.Equal(new QueenSlimeLootExecutionResult(0, 1, 2, 0), result);
        rolls.AssertConsumed();
    }

    [Fact]
    public void Master_relic_uses_common_luck_and_pets_use_independent_player_ordered_raw_rolls()
    {
        var rolls = new ScriptedRolls("N0:1=0", "N1:2=1", "L1=0", "N1:2=1",
            "N1:2=1", "N0:4=3", "N0:4=0", "L10=0", "N1:2=1");
        var sink = new RecordingSink();
        VanillaQueenSlimeLootPlayer[] players = [new(new(1), 10, 20), new(new(4), 30, 40)];
        Assert.True(VanillaQueenSlimeLootEvaluator.TryExecute(new(true, true), new(100, 200),
            players, rolls, sink, out QueenSlimeLootExecutionResult result));
        Assert.Equal([4950, 4960, 4958], sink.World.Select(static entry => entry.Drop.ItemType.Value));
        Assert.Equal(new NpcLootWorldItemOrigin(30, 40), sink.World[1].Origin);
        Assert.Equal(new QueenSlimeLootExecutionResult(3, 1, 2, 1), result);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Invalid_inputs_or_unsupported_capability_reject_before_rng_or_delivery(int invalid)
    {
        var rolls = new ScriptedRolls();
        var sink = new RecordingSink { Unsupported = invalid == 5 ? new ItemTypeId(4758) : default };
        var context = invalid == 0 ? new VanillaQueenSlimeLootContext(false, true) : new(false, false);
        var origin = invalid == 1 ? new NpcLootWorldItemOrigin(float.NaN, 0) : new(100, 200);
        VanillaQueenSlimeLootPlayer[] players = invalid switch
        {
            2 => [new(new(4), 10, 20), new(new(1), 30, 40)],
            3 => [new(new(1), 10, 20), new(new(1), 30, 40)],
            4 => [new(new(255), 10, 20)],
            _ => []
        };
        Assert.False(VanillaQueenSlimeLootEvaluator.TryExecute(in context, in origin,
            players, rolls, sink, out _));
        Assert.Empty(sink.World);
        Assert.Empty(sink.Instanced);
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(4758, 26, 28)]
    [InlineData(4950, 14, 14)]
    [InlineData(4957, 24, 24)]
    [InlineData(4958, 30, 30)]
    [InlineData(4959, 18, 18)]
    [InlineData(4960, 16, 30)]
    [InlineData(4980, 18, 28)]
    [InlineData(4981, 10, 32)]
    [InlineData(4982, 18, 18)]
    [InlineData(4983, 18, 18)]
    [InlineData(4984, 18, 18)]
    [InlineData(4986, 18, 20)]
    public void Drops_have_source_dimensions_and_do_not_admit_unverified_item_use(int id, int width, int height)
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(new(id), out VanillaItemDefinition item));
        Assert.Equal(new VanillaItemRuntimeDefaults(width, height, 9999), item.RuntimeDefaults);
        Assert.Null(item.UseTiming);
        Assert.Null(item.Placement);
        Assert.Null(item.PickTool);
        VanillaItemWorldDropDefinition drop = Assert.IsType<VanillaItemWorldDropDefinition>(item.WorldDrop);
        Assert.Equal((width, height, false), (drop.Width, drop.Height, drop.NoGravity));
        Assert.Equal(id == 4758 ? VanillaItemPrefixFamily.Summon : VanillaItemPrefixFamily.None, drop.PrefixFamily);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(new(id)));
    }

    [Fact]
    public void Blade_staff_damage_six_zero_knockback_leaves_exactly_seven_prefixes()
    {
        int[] valid = [87, 88, 92, 95, 96, 53, 40];
        foreach (PrefixId prefix in VanillaItemPrefixCatalog.GetRollablePrefixes(VanillaItemPrefixFamily.Summon))
            Assert.Equal(valid.Contains(prefix.Value),
                VanillaItemPrefixCatalog.IsValidForItem(VanillaQueenSlimeItemIds.BladeStaff, prefix));
        Assert.True(VanillaItemPrefixCatalog.IsValidForItem(VanillaQueenSlimeItemIds.BladeStaff, default));
        Assert.False(VanillaItemPrefixCatalog.IsValidForItem(new(999), VanillaPrefixIds.Worthy));
    }

    [Fact]
    public void Blade_staff_rerolls_invalid_knockback_then_invalid_rounding_before_materialization_velocity()
    {
        var rolls = new ScriptedRolls(
            "N0:4=1", "N0:22=0", // Fabled: knockback modifier forbidden.
            "N0:4=1", "N0:22=4", // Patient: 6 * .95 rounds to 6.
            "N0:4=1", "N0:22=2", // Worthy.
            "N-30:31=12", "N-40:-15=-20");
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(new(100, 200),
            new(VanillaQueenSlimeItemIds.BladeStaff, 1), rolls, out WorldItemDropStateUpdate drop));
        Assert.Equal((byte)87, drop.Prefix);
        Assert.Equal((87f, 186f), (drop.PositionX, drop.PositionY));
        Assert.Equal((1.2f, -2f), (drop.VelocityX, drop.VelocityY));
        rolls.AssertConsumed();
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    public void Blade_staff_damaged_prefix_preserves_reduced_natural_chance(int reducedRoll, int prefix)
    {
        var rolls = new ScriptedRolls("N0:4=1", "N0:22=18", $"N0:3={reducedRoll}");
        Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(VanillaQueenSlimeItemIds.BladeStaff, rolls, out PrefixId actual));
        Assert.Equal(prefix, actual.Value);
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

    private sealed class RecordingSink : IQueenSlimeLootDeliverySink
    {
        public ItemTypeId Unsupported { get; init; }
        public List<(NpcLootDrop Drop, NpcLootWorldItemOrigin Origin)> World { get; } = [];
        public List<NpcLootDrop> Instanced { get; } = [];
        public VanillaQueenSlimeLootPlayer[] Recipients { get; private set; } = [];
        public bool CanDeliverInstanced(ItemTypeId itemType) => CanDeliverWorldItem(itemType);
        public bool CanDeliverWorldItem(ItemTypeId itemType) =>
            itemType != Unsupported && VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(itemType);
        public bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
            ReadOnlySpan<VanillaQueenSlimeLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random)
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
