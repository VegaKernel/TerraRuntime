using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class PlayerBuffCurrentEffects1458Tests
{
    [Fact]
    public void Final_owned_Weak_keeps_indexed_food_order_for_one_update_without_a_second_clock()
    {
        var state = new PlayerBuffState();
        Assert.True(state.TryApplyNpcContactBuff(new BuffTypeId(33), 1));
        Assert.True(state.TryApplySelectedConsumable(new BuffTypeId(26), 120, false));
        Assert.True(state.TickNpcContactBuffs());

        Assert.Equal(0, state.GetDuration(new BuffTypeId(33)));
        Assert.Equal(120, state.GetDuration(new BuffTypeId(26)));
        Assert.Equal(new[] { new BuffTypeId(33), new BuffTypeId(26) }, state.CaptureEffectTypes());
        Assert.True(state.HasOwnedNpcContactBuffs);
        var exported = state.CaptureEffectTypes();
        exported[0] = default;
        Assert.Equal(new BuffTypeId(33), state.CaptureEffectTypes()[0]);

        Assert.False(state.TickNpcContactBuffs());
        Assert.False(state.HasOwnedNpcContactBuffs);
        Assert.True(state.HasOwnedFoodSlots);
        Assert.Equal(new[] { new BuffTypeId(26) }, state.CaptureEffectTypes());
        Assert.Equal(120, state.GetDuration(new BuffTypeId(26)));
    }

    [Fact]
    public void Late_cure_retains_current_Silence_and_semantic_currentness_until_the_next_update()
    {
        var types = new BuffTypeId[PlayerBuffState.Capacity];
        var durations = new int[PlayerBuffState.Capacity];
        types[0] = new BuffTypeId(35);
        durations[0] = 120;
        var state = PlayerBuffState.FromSlots(types, durations);
        var immune = VanillaPlayerCombatSnapshot.Baseline with { AnkhShield = true };
        Assert.True(state.RemoveEquipmentImmuneBuffs(in immune));
        Assert.Equal(0, state.CountActive(new BuffTypeId(35)));
        Assert.True(state.HasSilenceEffect);
        Assert.True(state.HasOwnedNpcContactBuffs);
        Assert.True(state.HasSameSlots(state.Clone()));

        var physicallyEmpty = PlayerBuffState.FromSlots(new BuffTypeId[PlayerBuffState.Capacity], new int[PlayerBuffState.Capacity]);
        Assert.False(state.HasSameSlots(physicallyEmpty));
        Assert.False(state.TickNpcContactBuffs());
        Assert.False(state.HasSilenceEffect);
        Assert.False(state.HasOwnedNpcContactBuffs);
        Assert.True(state.HasSameSlots(physicallyEmpty));
    }

    [Fact]
    public void Death_transfer_and_explicit_snapshot_reset_cannot_keep_a_previous_frame_Slow_effect()
    {
        var state = new PlayerBuffState();
        Assert.True(state.TryApplyNpcContactBuff(new BuffTypeId(32), 1));
        Assert.True(state.TickNpcContactBuffs());
        Assert.True(state.HasSlowEffect);

        var transferred = state.Clone();
        transferred.ResetOwnedFoodClockForTransfer();
        Assert.False(transferred.HasSlowEffect);
        Assert.False(transferred.HasOwnedNpcContactBuffs);

        var replaced = state.Clone();
        replaced.ReplaceNetworkSnapshot([]);
        Assert.False(replaced.HasSlowEffect);
        Assert.False(replaced.HasOwnedNpcContactBuffs);

        state.ClearNonPersistentOnDeath();
        Assert.False(state.HasSlowEffect);
        Assert.False(state.HasOwnedNpcContactBuffs);
    }
}
