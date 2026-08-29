using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcLootRuleTests
{
    [Fact]
    public void Blue_slime_rules_match_pinned_source_order_and_values()
    {
        ReadOnlySpan<VanillaNpcLootRule> rules =
            VanillaNpcLootRuleCatalog.GetNpcSpecificRules(VanillaNpcIds.BlueSlime);

        Assert.Equal(2, rules.Length);

        VanillaNpcLootRule gel = rules[0];
        Assert.Equal(VanillaNpcLootRuleKind.ExtraGel, gel.Kind);
        Assert.Equal(VanillaItemIds.Gel, gel.ItemType);
        Assert.Equal(1, gel.NormalChanceDenominator);
        Assert.Equal(1, gel.ExpertChanceDenominator);
        Assert.Equal((short)1, gel.MinimumStack);
        Assert.Equal((short)2, gel.MaximumStack);
        Assert.Equal((short)2, gel.ExtraGelMultiplier);

        VanillaNpcLootRule staff = rules[1];
        Assert.Equal(VanillaNpcLootRuleKind.NormalVsExpertCommon, staff.Kind);
        Assert.Equal(VanillaItemIds.SlimeStaff, staff.ItemType);
        Assert.Equal(10_000, staff.NormalChanceDenominator);
        Assert.Equal(7_000, staff.ExpertChanceDenominator);
        Assert.Equal((short)1, staff.MinimumStack);
        Assert.Equal((short)1, staff.MaximumStack);
    }

    [Fact]
    public void Normal_blue_slime_evaluation_preserves_luck_then_stack_rng_order()
    {
        var rolls = new ScriptedRollSource(
            luckResults: new[] { 0, 1 },
            randomResults: new[] { 2 });
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[2];
        var context = new VanillaNpcLootContext(IsExpertMode: false, DropExtraGel: false);

        Assert.True(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificRules(
            VanillaNpcIds.BlueSlime,
            in context,
            rolls,
            drops,
            out int count));

        Assert.Equal(1, count);
        Assert.Equal(new NpcLootDrop(VanillaItemIds.Gel, 2), drops[0]);
        Assert.Equal(new[] { "luck:1", "rng:1:3", "luck:10000" }, rolls.Calls);
    }

    [Fact]
    public void Drop_extra_gel_condition_selects_doubled_common_drop_range()
    {
        var rolls = new ScriptedRollSource(
            luckResults: new[] { 0, 1 },
            randomResults: new[] { 4 });
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[2];
        var context = new VanillaNpcLootContext(IsExpertMode: false, DropExtraGel: true);

        Assert.True(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificRules(
            VanillaNpcIds.BlueSlime,
            in context,
            rolls,
            drops,
            out int count));

        Assert.Equal(1, count);
        Assert.Equal(new NpcLootDrop(VanillaItemIds.Gel, 4), drops[0]);
        Assert.Equal(new[] { "luck:1", "rng:2:5", "luck:10000" }, rolls.Calls);
    }

    [Theory]
    [InlineData(false, 10000)]
    [InlineData(true, 7000)]
    public void Slime_staff_common_rule_uses_normal_or_expert_denominator(
        bool expert,
        int expectedDenominator)
    {
        var rolls = new ScriptedRollSource(
            luckResults: new[] { 1, 0 },
            randomResults: new[] { 1 });
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[2];
        var context = new VanillaNpcLootContext(IsExpertMode: expert, DropExtraGel: false);

        Assert.True(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificRules(
            VanillaNpcIds.BlueSlime,
            in context,
            rolls,
            drops,
            out int count));

        Assert.Equal(1, count);
        Assert.Equal(new NpcLootDrop(VanillaItemIds.SlimeStaff, 1), drops[0]);
        Assert.Equal(
            new[] { "luck:1", $"luck:{expectedDenominator}", "rng:1:2" },
            rolls.Calls);
    }

    [Fact]
    public void Two_successful_rules_emit_drops_in_registration_order()
    {
        var rolls = new ScriptedRollSource(
            luckResults: new[] { 0, 0 },
            randomResults: new[] { 1, 1 });
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[2];
        var context = new VanillaNpcLootContext(IsExpertMode: false, DropExtraGel: false);

        Assert.True(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificRules(
            VanillaNpcIds.BlueSlime,
            in context,
            rolls,
            drops,
            out int count));

        Assert.Equal(2, count);
        Assert.Equal(VanillaItemIds.Gel, drops[0].ItemType);
        Assert.Equal(VanillaItemIds.SlimeStaff, drops[1].ItemType);
    }

    [Fact]
    public void Unsupported_npc_and_short_destination_fail_without_consuming_rng()
    {
        var rolls = new ScriptedRollSource(Array.Empty<int>(), Array.Empty<int>());
        Span<NpcLootDrop> enough = stackalloc NpcLootDrop[2];
        Span<NpcLootDrop> shortDestination = stackalloc NpcLootDrop[1];
        var context = default(VanillaNpcLootContext);

        Assert.False(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificRules(
            VanillaNpcIds.DemonEye,
            in context,
            rolls,
            enough,
            out int unsupportedCount));
        Assert.Equal(0, unsupportedCount);

        Assert.False(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificRules(
            VanillaNpcIds.BlueSlime,
            in context,
            rolls,
            shortDestination,
            out int shortCount));
        Assert.Equal(0, shortCount);
        Assert.Empty(rolls.Calls);
    }

    private sealed class ScriptedRollSource : INpcLootRollSource
    {
        private readonly Queue<int> luckResults;
        private readonly Queue<int> randomResults;

        public ScriptedRollSource(IEnumerable<int> luckResults, IEnumerable<int> randomResults)
        {
            this.luckResults = new Queue<int>(luckResults);
            this.randomResults = new Queue<int>(randomResults);
        }

        public List<string> Calls { get; } = [];

        public int RollLuck(int chanceDenominator)
        {
            Calls.Add($"luck:{chanceDenominator}");
            return luckResults.Dequeue();
        }

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Calls.Add($"rng:{inclusiveMin}:{exclusiveMax}");
            int value = randomResults.Dequeue();
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
