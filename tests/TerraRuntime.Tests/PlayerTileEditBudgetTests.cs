using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class PlayerTileEditBudgetTests
{
    [Fact]
    public void Per_slot_budget_rejects_ninth_edit_in_same_tick()
    {
        var budget = new PlayerTileEditBudget(256);
        var slot = new PlayerSlotId(3);

        for (int i = 0; i < 8; i++)
            Assert.True(budget.TryConsume(slot));

        Assert.False(budget.TryConsume(slot));
    }

    [Fact]
    public void Different_player_slots_have_independent_budgets()
    {
        var budget = new PlayerTileEditBudget(256);
        var saturated = new PlayerSlotId(7);
        var other = new PlayerSlotId(8);

        for (int i = 0; i < 8; i++)
            Assert.True(budget.TryConsume(saturated));

        Assert.False(budget.TryConsume(saturated));
        Assert.True(budget.TryConsume(other));
    }

    [Fact]
    public void Advancing_to_a_new_tick_resets_consumed_slots_once()
    {
        var budget = new PlayerTileEditBudget(256);
        var slot = new PlayerSlotId(11);

        for (int i = 0; i < 8; i++)
            Assert.True(budget.TryConsume(slot));

        budget.AdvanceTo(0);
        Assert.False(budget.TryConsume(slot));

        budget.AdvanceTo(1);
        Assert.True(budget.TryConsume(slot));
    }

    [Fact]
    public void Slot_outside_configured_capacity_fails_closed()
    {
        var budget = new PlayerTileEditBudget(1);

        Assert.True(budget.TryConsume(new PlayerSlotId(0)));
        Assert.False(budget.TryConsume(new PlayerSlotId(1)));
    }
}
