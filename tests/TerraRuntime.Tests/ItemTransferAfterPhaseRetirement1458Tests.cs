using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class ItemTransferAfterPhaseRetirement1458Tests
{
    [Fact]
    public void Ordinary_contact_pickup_survives_retirement_of_unrelated_player_entropy()
    {
        using var fixture = new OwnedHumanPickupAuthority1458Tests.Fixture();
        fixture.Set(1, 758, 1);
        fixture.Member.SelectedItem = 1;
        fixture.State.Tick();
        Assert.False(fixture.Players.HasOwnedPickupContext);
        Assert.Null(fixture.Member.ItemPhase);
        fixture.Member.SelectedItem = 0;
        var item = fixture.Drop(9, 3);
        fixture.Pickup(item);
        Assert.False(fixture.Items.TryGetActive(item.Handle.Slot, out _));
        Assert.True(fixture.Players.TryGetInventoryItem(fixture.Connection, 0, out var received));
        Assert.Equal(3, received.Stack);
        Assert.False(fixture.Players.HasOwnedPickupContext);
        Assert.Null(fixture.Member.ItemPhase);
    }

    [Fact]
    public void Picked_up_stock_can_move_to_the_cursor_after_player_entropy_is_retired()
    {
        using var fixture = new OwnedHumanPickupAuthority1458Tests.Fixture();
        fixture.Pickup(fixture.Drop(9, 3));
        fixture.Set(1, 758, 1);
        fixture.Member.SelectedItem = 1;
        fixture.State.Tick();
        Assert.Null(fixture.Member.ItemPhase);
        fixture.Member.SelectedItem = 0;
        fixture.Member.ItemAnimation = 0;
        fixture.Set(0, 0, 0);
        fixture.Set(58, 9, 3);
        var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority)
            .GetField("inventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(fixture.Players)!;
        Assert.True(store.TryGetFundedQuantity(fixture.Connection, 58, out var funded));
        Assert.Equal(3, funded);
        Assert.False(fixture.Players.HasOwnedPickupContext);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(3)]
    [InlineData(23)]
    [InlineData(110)]
    [InlineData(71)]
    [InlineData(72)]
    public async Task Pickup_cursor_throw_and_pickup_repeats_without_duplication_after_retirement(short type)
    {
        using var fixture = new OwnedHumanPickupAuthority1458Tests.Fixture();
        fixture.Pickup(fixture.Drop(type, 3));
        fixture.Set(1, 758, 1);
        fixture.Member.SelectedItem = 1;
        fixture.State.Tick();
        fixture.Member.SelectedItem = 0;
        fixture.Member.ItemAnimation = 0;
        Assert.Null(fixture.Member.ItemPhase);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var image = fixture.ItemImage();
            int slot = Array.FindIndex(image, item => item.ItemType.Value == type);
            Assert.InRange(slot, 0, 57);
            Assert.Equal(3, image[slot].Stack);
            fixture.Set((short)slot, 0, 0);
            fixture.Set(58, type, 3);
            var actor = fixture.Member.CaptureSnapshot();
            int direction = (actor.ControlFlags & 64) != 0 ? 1 : -1;
            var proposal = new WorldItemDropStateUpdate(actor.PositionX + 2, actor.PositionY + 13,
                direction * 4 + actor.VelocityX, -2, 3, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, type, false, 0, 100);
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            fixture.State.Apply(new WorldItemAllocateRuntimeCommand(fixture.Connection, proposal, completion));
            var dropped = await completion.Task;
            Assert.NotNull(dropped);
            Assert.True(fixture.ItemImage()[58].IsEmpty);
            var replay = new TaskCompletionSource<WorldItemSnapshot?>();
            fixture.State.Apply(new WorldItemAllocateRuntimeCommand(fixture.Connection, proposal, replay));
            Assert.Null(await replay.Task);
            Assert.Equal(1, fixture.Items.ActiveCount);
            fixture.Pickup(dropped.Value);
            Assert.True(fixture.Items.TryGetActive(dropped.Value.Handle.Slot, out _));
            for (int tick = 0; tick < 100; tick++)
            {
                fixture.Items.TickReservationTimers();
                fixture.Players.TickItemAnimation();
            }
            // Controlled source contact after the genuine grab-delay clock expires.
            // Full item flight is covered by the existing movement suite.
            Assert.True(fixture.Items.TryApplyOwner(dropped.Value.Handle.Slot,
                new(0, 15, 0, 0, actor.PositionX, actor.PositionY), out _));
            fixture.Pickup(dropped.Value);
            Assert.Equal(0, fixture.Items.ActiveCount);
            Assert.Equal(3, fixture.ItemImage().Where(item => item.ItemType.Value == type).Sum(item => item.Stack));
            fixture.State.Apply(new WorldItemRemoveRuntimeCommand(fixture.Connection, dropped.Value.Handle));
            Assert.Equal(3, fixture.ItemImage().Where(item => item.ItemType.Value == type).Sum(item => item.Stack));
            Assert.False(fixture.Players.HasOwnedPickupContext);
            Assert.Null(fixture.Member.ItemPhase);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Retired_player_context_does_not_relax_pickup_contact_or_actor_validation(int refusal)
    {
        using var fixture = new OwnedHumanPickupAuthority1458Tests.Fixture();
        fixture.Set(1, 758, 1); fixture.Member.SelectedItem = 1; fixture.State.Tick();
        fixture.Member.SelectedItem = 0;
        if (refusal == 0) fixture.Member.IsDead = true;
        if (refusal == 1) fixture.Member.MovementFlags |= TerraRuntime.Gameplay.Players.VanillaPlayerHealthContext1458.GhostMovementFlag;
        var item = fixture.Drop(9, 3, x: refusal == 2 ? fixture.Member.PositionX + 40 : fixture.Member.PositionX);
        fixture.Pickup(item);
        Assert.True(fixture.Items.TryGetActive(item.Handle.Slot, out _));
        Assert.True(fixture.ItemImage()[0].IsEmpty);
    }

    [Fact]
    public async Task Proven_inventory_stock_can_be_thrown_without_reopening_the_retired_player_stream()
    {
        using var fixture = new OwnedHumanPickupAuthority1458Tests.Fixture();
        fixture.Pickup(fixture.Drop(9, 3));
        Assert.True(fixture.Players.TryGetInventoryItem(fixture.Connection, 0, out var received));
        Assert.Equal(3, received.Stack);
        fixture.Set(1, 758, 1);
        fixture.Member.SelectedItem = 1;
        fixture.State.Tick();
        Assert.False(fixture.Players.HasOwnedPickupContext);
        Assert.Null(fixture.Member.ItemPhase);
        fixture.Member.SelectedItem = 0;
        fixture.Member.ItemAnimation = 0;
        var state = fixture.Member.CaptureSnapshot();
        int direction = (state.ControlFlags & 64) != 0 ? 1 : -1;
        var proposal = new WorldItemDropStateUpdate(state.PositionX + 2, state.PositionY + 13,
            4 * direction + state.VelocityX, -2, 3, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, 9, false, 0, 100);
        var completion = new TaskCompletionSource<WorldItemSnapshot?>();
        fixture.State.Apply(new WorldItemAllocateRuntimeCommand(fixture.Connection, proposal, completion));
        Assert.NotNull(await completion.Task);
        Assert.True(fixture.Players.TryGetInventoryItem(fixture.Connection, 0, out var empty));
        Assert.True(empty.IsEmpty);
        Assert.False(fixture.Players.HasOwnedPickupContext);
        Assert.Null(fixture.Member.ItemPhase);
    }
}
