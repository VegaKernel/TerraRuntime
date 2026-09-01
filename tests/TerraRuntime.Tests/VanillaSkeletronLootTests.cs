using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaSkeletronLootTests
{
    [Fact]
    public void Classic_failure_chain_stops_after_first_success_then_runs_global_rules()
    {
        var rolls = new QueueRolls([1, 0, 1, 1], [1]);
        var sink = new RecordingSink();
        var context = new VanillaSkeletronLootContext(false, false, false);
        var origin = new NpcLootWorldItemOrigin(100f, 120f);

        Assert.True(VanillaSkeletronLootEvaluator.TryExecute(
            in context, in origin, [], rolls, sink, out SkeletronLootExecutionResult result));

        Assert.Equal(1, result.WorldItemCount);
        NpcLootDrop drop = Assert.Single(sink.WorldDrops);
        Assert.Equal(VanillaSkeletronItemIds.SkeletronHand, drop.ItemType);
        Assert.Equal((short)1, drop.Stack);
    }

    [Fact]
    public void Expert_delivers_one_instanced_bag_to_source_ordered_interactors()
    {
        var rolls = new QueueRolls([1, 1], [0, 1]);
        var sink = new RecordingSink();
        VanillaSkeletronLootPlayer[] players =
        [
            new(new PlayerSlotId(0), 100f, 100f),
            new(new PlayerSlotId(1), 120f, 100f)
        ];
        var context = new VanillaSkeletronLootContext(true, false, false);
        var origin = new NpcLootWorldItemOrigin(10f, 20f);

        Assert.True(VanillaSkeletronLootEvaluator.TryExecute(
            in context, in origin, players, rolls, sink, out SkeletronLootExecutionResult result));

        Assert.Equal(1, result.InstancedItemCount);
        Assert.Equal(2, result.InstancedRecipientCount);
        NpcLootDrop bag = Assert.Single(sink.InstancedDrops);
        Assert.Equal(VanillaSkeletronItemIds.SkeletronBossBag, bag.ItemType);
        Assert.Empty(sink.WorldDrops);
    }

    [Fact]
    public void RedHat_condition_emits_all_five_registered_vanity_items()
    {
        var rolls = new QueueRolls([1, 1, 1, 1], [1, 1, 1, 1, 1]);
        var sink = new RecordingSink();
        var context = new VanillaSkeletronLootContext(false, false, true);
        var origin = new NpcLootWorldItemOrigin(10f, 20f);

        Assert.True(VanillaSkeletronLootEvaluator.TryExecute(
            in context, in origin, [], rolls, sink, out SkeletronLootExecutionResult result));

        Assert.Equal(5, result.WorldItemCount);
        Assert.Equal(
            [
                VanillaSkeletronItemIds.ChippysHead,
                VanillaSkeletronItemIds.ChippysBody,
                VanillaSkeletronItemIds.ChippysLegs,
                VanillaSkeletronItemIds.ChippysWingsInactive,
                VanillaSkeletronItemIds.ChippysHeadband
            ],
            sink.WorldDrops.Select(static drop => drop.ItemType).ToArray());
    }

    [Theory]
    [InlineData(1273, 30, 10)]
    [InlineData(1281, 28, 20)]
    [InlineData(1313, 24, 28)]
    [InlineData(1363, 30, 30)]
    [InlineData(3323, 24, 24)]
    [InlineData(4801, 16, 30)]
    [InlineData(4927, 14, 14)]
    [InlineData(4993, 20, 20)]
    [InlineData(5624, 18, 14)]
    [InlineData(5625, 18, 14)]
    [InlineData(5626, 18, 14)]
    [InlineData(5628, 26, 30)]
    [InlineData(5737, 24, 8)]
    public void Skeletron_drop_item_defaults_are_materializable(int rawType, int width, int height)
    {
        ItemTypeId type = new(rawType);
        Assert.True(VanillaItemDefinitionCatalog.TryGetRuntimeDefaults(type, out VanillaItemRuntimeDefaults defaults));
        Assert.Equal(width, defaults.Width);
        Assert.Equal(height, defaults.Height);
        Assert.Equal(VanillaItemDefinitionCatalog.CommonMaximumStack, defaults.MaximumStack);
        Assert.True(VanillaItemDefinitionCatalog.TryGetWorldDrop(type, out _));
    }

    private sealed class RecordingSink : ISkeletronLootDeliverySink
    {
        public List<NpcLootDrop> WorldDrops { get; } = [];
        public List<NpcLootDrop> InstancedDrops { get; } = [];

        public bool CanDeliverInstanced(ItemTypeId itemType) => true;
        public bool CanDeliverWorldItem(ItemTypeId itemType) => true;

        public bool TryDeliverInstanced(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            ReadOnlySpan<VanillaSkeletronLootPlayer> recipients,
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
