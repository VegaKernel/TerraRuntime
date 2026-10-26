using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class PlayerItemUseBlockFacts1458Tests
{
    [Fact]
    public void All_seven_gem_staves_use_fresh_contact_gate_without_revoking_paid_frames() =>
        GemStaffBoundaryChecks.FreshCrowdControl();

    [Theory]
    [InlineData(23)]
    [InlineData(47)]
    [InlineData(149)]
    [InlineData(156)]
    public void Contact_blocking_effect_survives_final_update_clone_and_clears_on_next_update_or_death(int type)
    {
        var state = new PlayerBuffState();
        Assert.True(state.TryApplyNpcContactBuff(new(type), 1));
        Assert.True(state.PreventsFreshItemUse);
        Assert.Equal(type == 23, state.HasNoItemsEffect);
        Assert.Equal(type != 23, state.IsItemUseCrowdControlled);

        state.TickNpcContactBuffs();

        Assert.Equal(0, state.GetDuration(new(type)));
        Assert.Equal(0, state.CountActive(new(type)));
        Assert.True(state.PreventsFreshItemUse);
        Assert.True(state.HasOwnedNpcContactBuffs);
        var final = state.Clone();
        Assert.True(final.HasSameSlots(state));
        Assert.True(final.PreventsFreshItemUse);
        var dead = final.Clone();
        dead.ClearNonPersistentOnDeath();
        Assert.False(dead.PreventsFreshItemUse);
        Assert.False(dead.HasOwnedNpcContactBuffs);

        state.TickNpcContactBuffs();

        Assert.False(state.PreventsFreshItemUse);
        Assert.False(state.HasOwnedNpcContactBuffs);
        Assert.False(final.HasSameSlots(state));
    }

    [Fact]
    public void Source_shimmer_slot_blocks_use_only_while_its_known_duration_is_positive()
    {
        var types = new BuffTypeId[PlayerBuffState.Capacity];
        var times = new int[PlayerBuffState.Capacity];
        types[0] = VanillaBuffIds.Shimmer;
        times[0] = 1;
        var state = PlayerBuffState.FromSlots(types, times);
        Assert.True(state.IsItemUseCrowdControlled);
        Assert.True(state.PreventsFreshItemUse);
        Assert.False(state.HasNoItemsEffect);
        times[0] = 0;
        Assert.False(PlayerBuffState.FromSlots(types, times).PreventsFreshItemUse);
    }
}
