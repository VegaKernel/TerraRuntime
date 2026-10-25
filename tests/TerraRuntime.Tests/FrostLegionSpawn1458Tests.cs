using TerraRuntime.Application;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class FrostLegionSpawn1458Tests
{
    [Fact]
    public void Source_frost_selector_retains_shared_zombie_offer_and_short_circuit_order()
    {
        // NPC.Spawner.SpawnAnNPC/GetZombieSettings 1.4.5.8: two distinct Next7 calls.
        foreach (int unusedZombieStyle in new[] { 0, 6 })
        {
            var first = new Offers([(7, unusedZombieStyle), (7, 0)]);
            Assert.Equal(145, RuntimeInvasionSpawn1458.SelectFrost(first).Value);
            first.AssertComplete();
            var second = new Offers([(7, unusedZombieStyle), (7, 1), (3, 0)]);
            Assert.Equal(143, RuntimeInvasionSpawn1458.SelectFrost(second).Value);
            second.AssertComplete();
            var third = new Offers([(7, unusedZombieStyle), (7, 6), (3, 2)]);
            Assert.Equal(144, RuntimeInvasionSpawn1458.SelectFrost(third).Value);
            third.AssertComplete();
        }
    }

    [Fact]
    public void Source_frost_rewards_use_existing_item_defaults_and_luck_before_stack_in_both_difficulties()
    {
        // ItemDropDatabase.RegisterMiscDrops registers Common593/1/5..10 for all three NPCs.
        foreach (int type in new[] { 143, 144, 145 })
        foreach (bool expert in new[] { false, true })
        foreach (int quantity in new[] { 5, 10 })
        {
            Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(type), out var table));
            Assert.Equal(1, table.RuleCount); Assert.Equal(1, table.MaximumDropCount);
            var rule = table.Rules[0];
            Assert.Equal(VanillaNpcLootRuleKind.NormalVsExpertCommon, rule.Kind);
            Assert.Equal(593, rule.ItemType.Value);
            Assert.Equal(1, rule.NormalChanceDenominator); Assert.Equal(1, rule.ExpertChanceDenominator);
            Assert.Equal(5, rule.MinimumStack); Assert.Equal(10, rule.MaximumStack);
            var rolls = new LootOffers(quantity);
            Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in rule, new(expert, false), rolls,
                out bool dropped, out var drop));
            Assert.True(dropped); Assert.Equal(593, drop.ItemType.Value); Assert.Equal(quantity, drop.Stack);
            Assert.Equal(2, rolls.Phase);
        }
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(new(593), out var existing));
        Assert.Equal(12, existing.Width); Assert.Equal(12, existing.Height);
    }

    [Fact]
    public void Source_roster_and_rewards_do_not_admit_unrepresented_ai38_or_charge_for_a_dead_end()
    {
        foreach (int type in new[] { 143, 144, 145 })
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            Assert.True(definition.DefinitionOnly);
            Assert.Equal(38, definition.AiStyle.Value);
            Assert.False(VanillaNpcAiCoverageCatalog.TryGet(new(type), out _));
            Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(type), out _));
        }
        Assert.False(RuntimeInvasionSpawn1458.IsFrostCohortReady());
    }

    private sealed class Offers((int Bound, int Value)[] expected) : IVanillaNpcRandom
    {
        private int index;
        public int NextInt32(int min, int max)
        {
            Assert.InRange(index, 0, expected.Length - 1);
            Assert.Equal(0, min); Assert.Equal(expected[index].Bound, max);
            return expected[index++].Value;
        }
        internal void AssertComplete() => Assert.Equal(expected.Length, index);
    }

    private sealed class LootOffers(int quantity) : INpcLootRollSource
    {
        internal int Phase;
        public int RollLuck(int range) { Assert.Equal(0, Phase++); Assert.Equal(1, range); return 0; }
        public int NextInt32(int min, int max) { Assert.Equal(1, Phase++); Assert.Equal(5, min); Assert.Equal(11, max); return quantity; }
    }
}
