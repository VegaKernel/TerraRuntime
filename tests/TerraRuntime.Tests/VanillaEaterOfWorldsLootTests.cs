using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEaterOfWorldsLootTests
{
    [Fact]
    public void Classic_non_boss_segment_only_runs_small_material_rules_in_source_order()
    {
        var rolls = new SequenceRolls([0, 0], [2, 4]);
        var sink = new RecordingSink();
        var context = new VanillaEaterOfWorldsLootContext(false, false, false);
        var origin = new NpcLootWorldItemOrigin(100f, 200f);

        Assert.True(VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context, in origin, [], rolls, sink, out EaterOfWorldsLootExecutionResult result));

        Assert.Equal(2, result.SegmentWorldItemCount);
        Assert.Equal(0, result.BossWorldItemCount);
        Assert.Equal(
            ["world:86:2:100:200", "world:56:4:100:200"],
            sink.Events);
        rolls.AssertExhausted();
    }

    [Fact]
    public void Classic_last_segment_appends_normal_boss_rules_then_trophy()
    {
        var rolls = new SequenceRolls(
            luck: [0, 0, 0, 0, 0, 0],
            raw: [1, 3, 40, 1, 1, 1]);
        var sink = new RecordingSink();
        var context = new VanillaEaterOfWorldsLootContext(false, false, true);
        var origin = new NpcLootWorldItemOrigin(10f, 20f);

        Assert.True(VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context, in origin, [], rolls, sink, out EaterOfWorldsLootExecutionResult result));

        Assert.Equal(2, result.SegmentWorldItemCount);
        Assert.Equal(4, result.BossWorldItemCount);
        Assert.Equal(
            [
                "world:86:1:10:20",
                "world:56:3:10:20",
                "world:56:40:10:20",
                "world:994:1:10:20",
                "world:2111:1:10:20",
                "world:1361:1:10:20"
            ],
            sink.Events);
        rolls.AssertExhausted();
    }

    [Fact]
    public void Master_last_segment_runs_small_rules_bag_relic_per_player_pet_then_trophy()
    {
        VanillaEaterOfWorldsLootPlayer[] players =
        [
            new(new PlayerSlotId(2), 20f, 30f),
            new(new PlayerSlotId(7), 70f, 80f)
        ];
        var rolls = new SequenceRolls(
            luck: [0, 0, 0, 0],
            raw: [2, 1, 0, 1, 1, 1, 0, 3, 1]);
        var sink = new RecordingSink();
        var context = new VanillaEaterOfWorldsLootContext(true, true, true);
        var origin = new NpcLootWorldItemOrigin(100f, 200f);

        Assert.True(VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context, in origin, players, rolls, sink, out EaterOfWorldsLootExecutionResult result));

        Assert.Equal(2, result.SegmentWorldItemCount);
        Assert.Equal(3, result.BossWorldItemCount);
        Assert.Equal(1, result.InstancedItemCount);
        Assert.Equal(2, result.InstancedRecipientCount);
        Assert.Equal(1, result.MasterPetDropCount);
        Assert.Equal(
            [
                "world:86:2:100:200",
                "world:56:1:100:200",
                "instanced:3320:1:2:54000",
                "world:4925:1:100:200",
                "world:4799:1:20:30",
                "world:1361:1:100:200"
            ],
            sink.Events);
        rolls.AssertExhausted();
    }

    private sealed class SequenceRolls(int[] luck, int[] raw) : INpcLootRollSource
    {
        private int luckIndex;
        private int rawIndex;

        public int RollLuck(int chanceDenominator)
        {
            if (luckIndex >= luck.Length)
                throw new Xunit.Sdk.XunitException("Luck RNG consumed more values than expected.");
            int value = luck[luckIndex++];
            Assert.InRange(value, 0, chanceDenominator - 1);
            return value;
        }

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            if (rawIndex >= raw.Length)
                throw new Xunit.Sdk.XunitException("Raw RNG consumed more values than expected.");
            int value = raw[rawIndex++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }

        public void AssertExhausted()
        {
            Assert.Equal(luck.Length, luckIndex);
            Assert.Equal(raw.Length, rawIndex);
        }
    }

    private sealed class RecordingSink : IEaterOfWorldsLootDeliverySink
    {
        public List<string> Events { get; } = [];

        public bool CanDeliverInstanced(ItemTypeId itemType) => true;
        public bool CanDeliverWorldItem(ItemTypeId itemType) => true;

        public bool TryDeliverInstanced(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> recipients,
            int slotLeaseTicks,
            INpcLootRollSource random)
        {
            Events.Add($"instanced:{drop.ItemType.Value}:{drop.Stack}:{recipients.Length}:{slotLeaseTicks}");
            return true;
        }

        public bool TryDeliverWorldItem(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            INpcLootRollSource random)
        {
            Events.Add($"world:{drop.ItemType.Value}:{drop.Stack}:{origin.CenterX:0}:{origin.CenterY:0}");
            return true;
        }
    }
}
