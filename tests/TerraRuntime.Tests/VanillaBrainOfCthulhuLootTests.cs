using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaBrainOfCthulhuLootTests
{
    [Fact]
    public void Classic_creeper_uses_two_thirds_material_rules_and_source_stack_bands()
    {
        var rolls = new QueueRolls([0, 0], [5, 12]);
        var sink = new RecordingSink();
        var context = new VanillaBrainOfCthulhuLootContext(false, false, VanillaNpcIds.BrainCreeper);
        var origin = new NpcLootWorldItemOrigin(10, 20);

        Assert.True(VanillaBrainOfCthulhuLootEvaluator.TryExecute(
            in context, in origin, [], rolls, sink, out BrainOfCthulhuLootExecutionResult result));

        Assert.Equal(2, result.WorldItemCount);
        Assert.Collection(
            sink.WorldDrops,
            drop => Assert.Equal((VanillaBrainOfCthulhuItemIds.TissueSample, (short)5), (drop.ItemType, drop.Stack)),
            drop => Assert.Equal((VanillaBrainOfCthulhuItemIds.CrimtaneOre, (short)12), (drop.ItemType, drop.Stack)));
    }

    [Fact]
    public void Expert_brain_delivers_one_instanced_bag_and_trophy_rule_remains_later()
    {
        var rolls = new QueueRolls([9], [0, 1]);
        var sink = new RecordingSink();
        VanillaBrainOfCthulhuLootPlayer[] players =
        [
            new(new PlayerSlotId(0), 100f, 100f),
            new(new PlayerSlotId(1), 120f, 100f)
        ];
        var context = new VanillaBrainOfCthulhuLootContext(true, false, VanillaNpcIds.BrainOfCthulhu);
        var origin = new NpcLootWorldItemOrigin(10, 20);

        Assert.True(VanillaBrainOfCthulhuLootEvaluator.TryExecute(
            in context, in origin, players, rolls, sink, out BrainOfCthulhuLootExecutionResult result));

        Assert.Equal(1, result.InstancedItemCount);
        Assert.Equal(2, result.InstancedRecipientCount);
        Assert.Empty(sink.WorldDrops);
        Assert.Single(sink.InstancedDrops);
        Assert.Equal(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuBossBag, sink.InstancedDrops[0].ItemType);
    }

    [Theory]
    [InlineData(880, 12, 12)]
    [InlineData(1329, 14, 18)]
    [InlineData(1362, 30, 30)]
    [InlineData(2104, 28, 20)]
    [InlineData(3060, 16, 30)]
    [InlineData(3321, 24, 24)]
    [InlineData(4800, 16, 30)]
    [InlineData(4926, 14, 14)]
    public void Brain_drop_item_defaults_are_materializable(int rawType, int width, int height)
    {
        ItemTypeId type = new(rawType);
        Assert.True(VanillaItemDefinitionCatalog.TryGetRuntimeDefaults(type, out VanillaItemRuntimeDefaults defaults));
        Assert.Equal(width, defaults.Width);
        Assert.Equal(height, defaults.Height);
        Assert.Equal(VanillaItemDefinitionCatalog.CommonMaximumStack, defaults.MaximumStack);
        Assert.True(VanillaItemDefinitionCatalog.TryGetWorldDrop(type, out _));
    }

    private sealed class RecordingSink : IBrainOfCthulhuLootDeliverySink
    {
        public List<NpcLootDrop> WorldDrops { get; } = [];
        public List<NpcLootDrop> InstancedDrops { get; } = [];

        public bool CanDeliverInstanced(ItemTypeId itemType) => true;
        public bool CanDeliverWorldItem(ItemTypeId itemType) => true;

        public bool TryDeliverInstanced(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            ReadOnlySpan<VanillaBrainOfCthulhuLootPlayer> recipients,
            int slotLeaseTicks,
            INpcLootRollSource random)
        {
            InstancedDrops.Add(drop);
            return true;
        }

        public bool TryDeliverWorldItem(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            INpcLootRollSource random)
        {
            WorldDrops.Add(drop);
            return true;
        }
    }

    private sealed class QueueRolls(IEnumerable<int> luckValues, IEnumerable<int> intValues) : INpcLootRollSource
    {
        private readonly Queue<int> luck = new(luckValues);
        private readonly Queue<int> ints = new(intValues);

        public int RollLuck(int chanceDenominator) => luck.Count == 0 ? chanceDenominator - 1 : luck.Dequeue();

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            if (ints.Count == 0)
                return inclusiveMin;
            int value = ints.Dequeue();
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
