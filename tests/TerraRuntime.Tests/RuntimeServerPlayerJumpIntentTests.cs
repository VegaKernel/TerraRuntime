using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerJumpIntentTests
{
    [Fact]
    public void Jump_control_is_bound_to_exact_generation_and_removed_on_reuse()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities);
        var firstId = new ServerPlayerId("test:first-jump");
        var secondId = new ServerPlayerId("test:second-jump");

        ServerPlayerCreateResult first = service.Create(firstId, 10f, 20f);
        Assert.True(first.IsCreated);
        Assert.True(service.SetJumpIntent(firstId, ServerPlayerJumpIntent.Held));
        service.CommitJumpState(first.Player, new VanillaServerPlayerJumpState(11, false));
        Assert.Equal(ServerPlayerJumpIntent.Held, service.GetJumpIntent(first.Player));
        Assert.Equal(11, service.GetJumpState(first.Player).RemainingTicks);

        Assert.True(service.Despawn(firstId));
        ServerPlayerCreateResult second = service.Create(secondId, 30f, 40f);

        Assert.True(second.IsCreated);
        Assert.Equal(first.Player.Slot, second.Player.Slot);
        Assert.True(second.Player.Generation.Value > first.Player.Generation.Value);
        Assert.Equal(ServerPlayerJumpIntent.Released, service.GetJumpIntent(first.Player));
        Assert.Equal(VanillaServerPlayerJumpState.Initial, service.GetJumpState(first.Player));
        Assert.Equal(ServerPlayerJumpIntent.Released, service.GetJumpIntent(second.Player));
        Assert.Equal(VanillaServerPlayerJumpState.Initial, service.GetJumpState(second.Player));
    }

    [Fact]
    public void Release_resets_sparse_jump_input_and_physics_state()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities);
        var id = new ServerPlayerId("test:release-jump");
        ServerPlayerCreateResult created = service.Create(id, 0f, 0f);
        Assert.True(created.IsCreated);

        Assert.True(service.SetJumpIntent(id, ServerPlayerJumpIntent.Held));
        service.CommitJumpState(created.Player, new VanillaServerPlayerJumpState(8, false));
        Assert.True(service.SetJumpIntent(id, ServerPlayerJumpIntent.Released));

        Assert.Equal(ServerPlayerJumpIntent.Released, service.GetJumpIntent(created.Player));
        Assert.Equal(VanillaServerPlayerJumpState.Initial, service.GetJumpState(created.Player));
        Assert.False(service.SetJumpIntent(id, (ServerPlayerJumpIntent)42));
    }

    [Fact]
    public void Missing_server_player_cannot_receive_jump_intent()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities);

        Assert.False(service.SetJumpIntent(
            new ServerPlayerId("test:missing-jump"),
            ServerPlayerJumpIntent.Held));
    }
}
