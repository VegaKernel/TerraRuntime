using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerMovementControllerTests
{
    [Theory]
    [InlineData(62, true)]
    [InlineData(72, false)]
    [InlineData(0, false)]
    public async Task Flight_authority_requires_functional_not_vanity_or_inventory_wings(short itemSlot, bool flies)
    {
        RuntimeFixture fixture = CreateFixture(1);
        var id = new ServerPlayerId("test:equipped-flight");
        ServerPlayerCreateResult created = await CreateAsync(fixture.Runtime, id, 96f, 600f);
        Assert.True(created.IsCreated);
        Assert.True(fixture.States.TrySetItem(created.Player,
            new ServerPlayerItemState(itemSlot, TerraRuntime.Contracts.Gameplay.VanillaItemIds.FishronWings,
                1, default, 0), out _));
        Assert.True(await SetIntentAsync(fixture.Runtime, id,
            ServerPlayerMovementIntent.MoveTo(106f, 0f, ServerPlayerMovementOptions.Default with { FlightEnabled = true })));
        for (int i = 0; i < 40; i++) fixture.Runtime.Tick();
        Assert.True(fixture.States.TryGet(created.Player, out var moved));
        Assert.Equal(flies, moved.VelocityY < -5f);
    }

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
        Assert.Equal((byte)((1 << 3) | (1 << 6)), moved.ControlFlags);
    }

    [Fact]
    public async Task MoveTo_turns_left_and_publishes_source_packet13_control_bits()
    {
        RuntimeFixture fixture = CreateFixture(1);
        var id = new ServerPlayerId("test:turn-left");
        ServerPlayerCreateResult created = await CreateAsync(fixture.Runtime, id, 96f, 80f);
        Assert.True(created.IsCreated);

        Assert.True(await SetIntentAsync(fixture.Runtime, id, ServerPlayerMovementIntent.MoveTo(0f, 101f)));
        fixture.Runtime.Tick();

        Assert.True(fixture.States.TryGet(created.Player, out PlayerStateSnapshot moved));
        Assert.Equal((byte)(1 << 2), moved.ControlFlags);
        Assert.True(moved.VelocityX < 0f);
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

    [Fact]
    public async Task Flight_enabled_keeps_jump_held_after_base_jump_expires()
    {
        RuntimeFixture fixture = CreateFixture(1);
        var id = new ServerPlayerId("test:flight");
        ServerPlayerCreateResult created = await CreateAsync(fixture.Runtime, id, 96f, 600f);
        Assert.True(created.IsCreated);
        // A movement option is policy, not permission to fly without functional wings.
        Assert.True(fixture.States.TrySetItem(created.Player,
            new ServerPlayerItemState(62, new TerraRuntime.Contracts.Gameplay.ItemTypeId(2609), 1,
                TerraRuntime.Contracts.Gameplay.VanillaPrefixIds.None, 0), out _));
        ServerPlayerMovementOptions options = ServerPlayerMovementOptions.Default with { FlightEnabled = true };

        Assert.True(await SetIntentAsync(
            fixture.Runtime,
            id,
            ServerPlayerMovementIntent.MoveTo(106f, 0f, options)));
        for (int i = 0; i < 30; i++)
            fixture.Runtime.Tick();

        Assert.True(fixture.States.TryGet(created.Player, out PlayerStateSnapshot flying));
        Assert.True((flying.ControlFlags & (1 << 4)) != 0);
        Assert.True(flying.VelocityY < -5f);
        Assert.True(flying.PositionY < 480f);
    }

    private static RuntimeFixture CreateFixture(int capacity)
    {
        var slots = new PlayerSlotPool(capacity);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var runtime = new ServerRuntimeState(
            worldTiles: tiles,
            serverPlayers: new ServerPlayerAuthority(states, identities, tiles));
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
        ServerPlayerStateStore States);
}
