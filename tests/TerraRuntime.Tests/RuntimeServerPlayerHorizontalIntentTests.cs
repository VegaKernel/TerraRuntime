using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerHorizontalIntentTests
{
    [Fact]
    public void Intent_is_bound_to_exact_player_generation_and_removed_on_reuse()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities);
        var firstId = new ServerPlayerId("test:first-horizontal");
        var secondId = new ServerPlayerId("test:second-horizontal");

        ServerPlayerCreateResult first = service.Create(firstId, 10f, 20f);
        Assert.True(first.IsCreated);
        Assert.True(service.SetHorizontalIntent(firstId, ServerPlayerHorizontalIntent.Right));
        Assert.Equal(ServerPlayerHorizontalIntent.Right, service.GetHorizontalIntent(first.Player));

        Assert.True(service.Despawn(firstId));
        ServerPlayerCreateResult second = service.Create(secondId, 30f, 40f);

        Assert.True(second.IsCreated);
        Assert.Equal(first.Player.Slot, second.Player.Slot);
        Assert.True(second.Player.Generation.Value > first.Player.Generation.Value);
        Assert.Equal(ServerPlayerHorizontalIntent.Stop, service.GetHorizontalIntent(first.Player));
        Assert.Equal(ServerPlayerHorizontalIntent.Stop, service.GetHorizontalIntent(second.Player));
    }

    [Fact]
    public void Stop_removes_sparse_control_state_and_unknown_values_are_rejected()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities);
        var id = new ServerPlayerId("test:stop-horizontal");
        ServerPlayerCreateResult created = service.Create(id, 0f, 0f);
        Assert.True(created.IsCreated);

        Assert.True(service.SetHorizontalIntent(id, ServerPlayerHorizontalIntent.Left));
        Assert.Equal(ServerPlayerHorizontalIntent.Left, service.GetHorizontalIntent(created.Player));

        Assert.True(service.SetHorizontalIntent(id, ServerPlayerHorizontalIntent.Stop));
        Assert.Equal(ServerPlayerHorizontalIntent.Stop, service.GetHorizontalIntent(created.Player));
        Assert.False(service.SetHorizontalIntent(id, (ServerPlayerHorizontalIntent)42));
        Assert.Equal(ServerPlayerHorizontalIntent.Stop, service.GetHorizontalIntent(created.Player));
    }

    [Fact]
    public void Missing_server_player_cannot_receive_horizontal_intent()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities);

        Assert.False(service.SetHorizontalIntent(
            new ServerPlayerId("test:missing-horizontal"),
            ServerPlayerHorizontalIntent.Right));
    }
}
