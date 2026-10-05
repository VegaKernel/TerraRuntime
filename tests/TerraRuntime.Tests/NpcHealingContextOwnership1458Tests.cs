using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;

namespace TerraRuntime.Tests;

public sealed class NpcHealingContextOwnership1458Tests
{
    [Theory]
    [InlineData(0, false)] [InlineData(1, true)]
    public void Unknown_health_rejects_only_when_the_common_heart_offer_is_selected(int heartRoll, bool admitted)
    {
        var context = new VanillaNpcHealingContext1458(VanillaNpcIds.Zombie, new(3), 100, 10,
            false, false, false, LifeEligibilityKnown: false);
        var sink = new Sink(); var random = new Rolls(heartRoll);
        var origin = new NpcLootWorldItemOrigin(500, 500);
        Assert.Equal(admitted, VanillaNpcHealingLoot1458.TryExecute(in context, in origin, random, sink));
        Assert.Equal(0, sink.Drops);
        Assert.Equal(2, random.NextCalls);
        Assert.Equal(admitted ? 2 : 1, random.LuckCalls);
    }

    [Fact]
    public void A_selected_mana_drop_does_not_require_unread_life_context()
    {
        var context = new VanillaNpcHealingContext1458(VanillaNpcIds.Zombie, new(3), 100, 10,
            false, true, false, LifeEligibilityKnown: false);
        var sink = new Sink(); var random = new Rolls(0);
        var origin = new NpcLootWorldItemOrigin(500, 500);
        Assert.True(VanillaNpcHealingLoot1458.TryExecute(in context, in origin, random, sink));
        Assert.Equal(1, sink.Drops); Assert.Equal(1, random.NextCalls);
    }

    private sealed class Rolls(int heartRoll) : INpcLootRollSource
    {
        internal int LuckCalls, NextCalls;
        public int RollLuck(int denominator) { LuckCalls++; return denominator == 6 ? 0 : 1; }
        public int NextInt32(int minimum, int maximum) { NextCalls++; return NextCalls == 1 ? 0 : heartRoll; }
    }
    private sealed class Sink : IBossRecoveryLootDeliverySink1458
    {
        internal int Drops;
        public bool CanDeliverWorldItem(ItemTypeId type) => true;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        { Drops++; return true; }
    }
}
