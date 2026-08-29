using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeServerPlayerHorizontalControlTests
{
    [Fact]
    public async Task Horizontal_intent_command_drives_authoritative_physics_tick()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            worldTiles: new WorldTileStore(new WorldDimensions(100, 100)),
            serverPlayerStates: states,
            serverPlayerIdentities: identities);
        var id = new ServerPlayerId("test:runtime-horizontal");

        var create = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, 96f, 80f, create));
        ServerPlayerCreateResult created = await create.Task;
        Assert.True(created.IsCreated);

        var horizontal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerHorizontalIntentRuntimeCommand(
            id,
            ServerPlayerHorizontalIntent.Right,
            horizontal));
        Assert.True(await horizontal.Task);

        runtime.Tick();

        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot moved));
        Assert.Equal(2UL, moved.Revision.Value);
        Assert.Equal(96.08f, moved.PositionX, 5);
        Assert.Equal(80.4f, moved.PositionY, 5);
        Assert.Equal(0.08f, moved.VelocityX, 5);
        Assert.Equal(0.4f, moved.VelocityY, 5);
    }

    [Fact]
    public async Task Invalid_intent_command_is_rejected_without_changing_default_stop_state()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            worldTiles: new WorldTileStore(new WorldDimensions(100, 100)),
            serverPlayerStates: states,
            serverPlayerIdentities: identities);
        var id = new ServerPlayerId("test:invalid-runtime-horizontal");

        var create = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, 96f, 80f, create));
        ServerPlayerCreateResult created = await create.Task;
        Assert.True(created.IsCreated);

        var horizontal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerHorizontalIntentRuntimeCommand(
            id,
            (ServerPlayerHorizontalIntent)42,
            horizontal));
        Assert.False(await horizontal.Task);

        runtime.Tick();

        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot moved));
        Assert.Equal(96f, moved.PositionX, 5);
        Assert.Equal(80.4f, moved.PositionY, 5);
        Assert.Equal(0f, moved.VelocityX, 5);
    }
}
