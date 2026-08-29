using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerMovementControllerTests
{
    [Fact]
    public async Task MoveTo_produces_horizontal_intent_that_flows_through_player_physics()
    {
        RuntimeFixture fixture = CreateFixture(1);
        var id = new ServerPlayerId("test:move-to");
        ServerPlayerCreateResult created = await CreateAsync(fixture.Runtime, id, 96f, 80f);
        Assert.True(created.IsCreated);
        var intent = ServerPlayerMovementIntent.MoveTo(targetX: 200f, targetY: 101f);

        Assert.True(await SetIntentAsync(fixture.Runtime, id, intent));
        fixture.Runtime.Tick();

        Assert.True(fixture.States.TryGet(created.Player, out PlayerStateSnapshot moved));
        Assert.Equal(96.08f, moved.PositionX, 5);
        Assert.Equal(0.08f, moved.VelocityX, 5);
        Assert.Equal(80.4f, moved.PositionY, 5);
    }

    [Fact]
    public async Task MoveTo_above_actor_holds_jump_but_never_writes_position_directly()
    {
        RuntimeFixture fixture = CreateFixture(1);
        var id = new ServerPlayerId("test:move-to-jump");
        ServerPlayerCreateResult created = await CreateAsync(fixture.Runtime, id, 96f, 80f);
        Assert.True(created.IsCreated);
        var intent = ServerPlayerMovementIntent.MoveTo(targetX: 106f, targetY: 0f);

        Assert.True(await SetIntentAsync(fixture.Runtime, id, intent));
        fixture.Runtime.Tick();

        Assert.True(fixture.States.TryGet(created.Player, out PlayerStateSnapshot moved));
        Assert.Equal(96f, moved.PositionX, 5);
        Assert.Equal(75.39f, moved.PositionY, 5);
        Assert.Equal(-4.61f, moved.VelocityY, 5);
    }

    [Fact]
    public async Task FollowPlayer_stops_when_exact_target_generation_despawns()
    {
        RuntimeFixture fixture = CreateFixture(2);
        var followerId = new ServerPlayerId("test:follower");
        var targetId = new ServerPlayerId("test:target");
        ServerPlayerCreateResult follower = await CreateAsync(fixture.Runtime, followerId, 96f, 80f);
        ServerPlayerCreateResult target = await CreateAsync(fixture.Runtime, targetId, 160f, 80f);
        Assert.True(follower.IsCreated);
        Assert.True(target.IsCreated);
        var intent = ServerPlayerMovementIntent.FollowPlayer(target.Player);
        Assert.True(await SetIntentAsync(fixture.Runtime, followerId, intent));

        fixture.Runtime.Tick();
        Assert.True(fixture.States.TryGet(follower.Player, out PlayerStateSnapshot following));
        Assert.Equal(96.08f, following.PositionX, 5);

        Assert.True(await DespawnAsync(fixture.Runtime, targetId));
        ServerPlayerCreateResult replacement = await CreateAsync(fixture.Runtime, targetId, 220f, 80f);
        Assert.True(replacement.IsCreated);
        Assert.Equal(target.Player.Slot, replacement.Player.Slot);
        Assert.NotEqual(target.Player.Generation, replacement.Player.Generation);

        fixture.Runtime.Tick();

        Assert.True(fixture.States.TryGet(follower.Player, out PlayerStateSnapshot stopped));
        Assert.Equal(following.PositionX, stopped.PositionX, 5);
        Assert.Equal(0f, stopped.VelocityX, 5);
    }

    private static RuntimeFixture CreateFixture(int capacity)
    {
        var slots = new PlayerSlotPool(capacity);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            worldTiles: new WorldTileStore(new WorldDimensions(100, 100)),
            serverPlayerStates: states,
            serverPlayerIdentities: identities);
        return new RuntimeFixture(runtime, states);
    }

    private static async Task<ServerPlayerCreateResult> CreateAsync(
        ServerRuntimeState runtime,
        ServerPlayerId id,
        float positionX,
        float positionY)
    {
        var completion = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, positionX, positionY, completion));
        return await completion.Task;
    }

    private static async Task<bool> SetIntentAsync(
        ServerRuntimeState runtime,
        ServerPlayerId id,
        ServerPlayerMovementIntent intent)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerMovementIntentRuntimeCommand(id, intent, completion));
        return await completion.Task;
    }

    private static async Task<bool> DespawnAsync(ServerRuntimeState runtime, ServerPlayerId id)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerDespawnRuntimeCommand(id, completion));
        return await completion.Task;
    }

    private sealed record RuntimeFixture(
        ServerRuntimeState Runtime,
        RuntimeServerPlayerStateStore States);
}
