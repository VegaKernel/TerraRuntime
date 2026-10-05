using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class UndeadLootCondition1458Tests
{
    public static IEnumerable<object[]> Zombies()
    {
        foreach (int id in new[] { 3, 591, 590, 331, 332, 132, 161, 186, 187, 188, 189, 200, 223,
                     319, 320, 321, 430, 431, 432, 433, 434, 435, 436 }) yield return [id];
    }

    [Theory, MemberData(nameof(Zombies))]
    public void Unknown_Sickle_facts_fence_all_registered_Zombie_tables_before_any_draw(int type)
    {
        Assert.True(VanillaUndeadLootCatalog1458.HasSickleCondition(new(type)));
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(type), out var table));
        var context = new VanillaNpcLootContext(false, false, LowTiles: true, HasSickle: null);
        Assert.False(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificTable(in table, in context,
            new NoDraws(), new NpcLootDrop[table.MaximumDropCount], out int count));
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(188)]
    [InlineData(189)]
    [InlineData(434)]
    [InlineData(435)]
    public void Proven_Sickle_possession_does_not_invent_the_independent_Wood_lowTiles_fact(int type)
    {
        Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(type), out var table));
        var context = new VanillaNpcLootContext(false, false, LowTiles: null, HasSickle: true);
        Assert.False(VanillaNpcLootEvaluator.TryEvaluateNpcSpecificTable(in table, in context,
            new NoDraws(), new NpcLootDrop[table.MaximumDropCount], out int count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void Failed_random_chain_stops_at_DidNotMeetConditions_without_luck_or_stack_draws()
    {
        var excluded = new VanillaNpcLootRule(VanillaNpcLootRuleKind.NotExpertCommon,
            VanillaItemIds.Bone, 1, 1, 1, 3, 1);
        var following = VanillaNpcLootRule.NormalVsExpertCommon(VanillaItemIds.GoldenKey, 1, 1);
        var chain = new VanillaNpcLootRule(VanillaNpcLootRuleKind.FailedRollChain,
            excluded.ItemType, 1, 1, 1, 1, 1, new([excluded, following]));
        var context = new VanillaNpcLootContext(true, false);
        Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in chain, in context, new NoDraws(),
            out bool dropped, out _));
        Assert.False(dropped);
    }

    [Fact]
    public void Rule_options_and_failed_roll_alternatives_own_copies_and_reject_recursive_graphs()
    {
        var first = VanillaNpcLootRule.NormalVsExpertCommon(VanillaItemIds.Bone, 1, 1);
        var second = VanillaNpcLootRule.NormalVsExpertCommon(VanillaItemIds.GoldenKey, 1, 1);
        var source = new[] { first, second };
        var alternatives = new VanillaNpcLootAlternatives1458(source);
        source[0] = default;
        Assert.Equal(first, alternatives.Rules[0]);
        var nested = new VanillaNpcLootRule(VanillaNpcLootRuleKind.FailedRollChain, first.ItemType,
            1, 1, 1, 1, 1, alternatives);
        Assert.Throws<ArgumentException>(() => new VanillaNpcLootAlternatives1458([nested, second]));
        var items = new[] { VanillaItemIds.Bone, VanillaItemIds.GoldenKey };
        var options = new VanillaNpcLootOptions1458(items);
        items[0] = default;
        Assert.Equal(VanillaItemIds.Bone, options.Items[0]);
    }

    private sealed class NoDraws : INpcLootRollSource
    {
        public int RollLuck(int denominator) => throw new InvalidOperationException("Unadmitted condition drew luck.");
        public int NextInt32(int minimum, int maximum) => throw new InvalidOperationException("Unadmitted condition drew RNG.");
    }
}
