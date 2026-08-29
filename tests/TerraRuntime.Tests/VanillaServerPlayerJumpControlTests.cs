using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class VanillaServerPlayerJumpControlTests
{
    [Fact]
    public void Held_from_released_grounded_state_starts_vanilla_jump()
    {
        VanillaServerPlayerJumpState state = VanillaServerPlayerJumpState.Initial;

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            0f,
            ServerPlayerJumpIntent.Held,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(-5.01f, velocityY, 5);
        Assert.Equal(15, next.RemainingTicks);
        Assert.False(next.ReleaseReady);
    }

    [Fact]
    public void Liquid_selected_profile_starts_jump_with_supplied_speed_and_height()
    {
        VanillaServerPlayerJumpState state = VanillaServerPlayerJumpState.Initial;

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            0f,
            ServerPlayerJumpIntent.Held,
            in state,
            VanillaServerPlayerLiquidPhysics.WaterJumpSpeed,
            VanillaServerPlayerLiquidPhysics.WaterJumpHeight,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(-6.01f, velocityY, 5);
        Assert.Equal(30, next.RemainingTicks);
        Assert.False(next.ReleaseReady);
    }

    [Fact]
    public void Liquid_selected_profile_reasserts_current_medium_jump_speed()
    {
        var state = new VanillaServerPlayerJumpState(23, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            -5.36f,
            ServerPlayerJumpIntent.Held,
            in state,
            VanillaServerPlayerLiquidPhysics.ShimmerJumpSpeed,
            VanillaServerPlayerLiquidPhysics.ShimmerJumpHeight,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(-5.51f, velocityY, 5);
        Assert.Equal(22, next.RemainingTicks);
        Assert.False(next.ReleaseReady);
    }

    [Fact]
    public void Held_active_jump_reasserts_jump_speed_and_decrements_counter()
    {
        var state = new VanillaServerPlayerJumpState(15, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            -4.61f,
            ServerPlayerJumpIntent.Held,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(-5.01f, velocityY, 5);
        Assert.Equal(14, next.RemainingTicks);
        Assert.False(next.ReleaseReady);
    }

    [Fact]
    public void Released_cancels_remaining_jump_and_arms_release_gate()
    {
        var state = new VanillaServerPlayerJumpState(9, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            -4.61f,
            ServerPlayerJumpIntent.Released,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(-4.61f, velocityY, 5);
        Assert.Equal(VanillaServerPlayerJumpState.Initial, next);
    }

    [Fact]
    public void Held_after_landing_without_release_does_not_restart_jump()
    {
        var state = new VanillaServerPlayerJumpState(0, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            0f,
            ServerPlayerJumpIntent.Held,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(0f, velocityY, 5);
        Assert.Equal(state, next);
    }

    [Fact]
    public void Held_active_jump_that_is_already_stopped_clears_counter_without_rearming_release()
    {
        var state = new VanillaServerPlayerJumpState(7, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            0f,
            ServerPlayerJumpIntent.Held,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(0f, velocityY, 5);
        Assert.Equal(new VanillaServerPlayerJumpState(0, false), next);
    }

    [Fact]
    public void Invalid_jump_profile_is_rejected()
    {
        VanillaServerPlayerJumpState state = VanillaServerPlayerJumpState.Initial;

        Assert.False(VanillaServerPlayerJumpControl.TryApply(
            0f,
            ServerPlayerJumpIntent.Held,
            in state,
            6.01f,
            VanillaServerPlayerJumpControl.MaximumSupportedJumpHeight + 1,
            out _,
            out _));
    }

    [Fact]
    public void Invalid_jump_intent_is_rejected()
    {
        VanillaServerPlayerJumpState state = VanillaServerPlayerJumpState.Initial;

        Assert.False(VanillaServerPlayerJumpControl.TryApply(
            0f,
            (ServerPlayerJumpIntent)42,
            in state,
            out _,
            out _));
    }
}
