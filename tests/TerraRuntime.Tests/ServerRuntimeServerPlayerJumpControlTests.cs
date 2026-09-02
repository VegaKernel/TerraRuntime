using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeServerPlayerJumpControlTests
{
    [Fact]
    public async Task Jump_intent_command_drives_authoritative_jump_before_gravity()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        WorldTileStore tiles = CreateGroundedWorld();
        var runtime = new ServerRuntimeState(
            worldTiles: tiles,
            serverPlayers: new ServerPlayerAuthority(states, identities, tiles));
        var id = new ServerPlayerId("test:runtime-jump");

        var create = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, 320f, 438f, create));
        ServerPlayerCreateResult created = await create.Task;
        Assert.True(created.IsCreated);

        var jump = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerJumpIntentRuntimeCommand(id, ServerPlayerJumpIntent.Held, jump));
        Assert.True(await jump.Task);

        runtime.Tick();

        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot moved));
        Assert.Equal(2UL, moved.Revision.Value);
        Assert.Equal(320f, moved.PositionX, 5);
        Assert.Equal(433.39f, moved.PositionY, 4);
        Assert.Equal(0f, moved.VelocityX, 5);
        Assert.Equal(-4.61f, moved.VelocityY, 4);
    }

    [Fact]
    public async Task Held_jump_does_not_restart_after_landing_until_release()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        WorldTileStore tiles = CreateGroundedWorld();
        var runtime = new ServerRuntimeState(
            worldTiles: tiles,
            serverPlayers: new ServerPlayerAuthority(states, identities, tiles));
        var id = new ServerPlayerId("test:held-jump-release-gate");

        var create = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, 320f, 438f, create));
        ServerPlayerCreateResult created = await create.Task;
        Assert.True(created.IsCreated);

        var jump = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerJumpIntentRuntimeCommand(id, ServerPlayerJumpIntent.Held, jump));
        Assert.True(await jump.Task);

        for (int tick = 0; tick < 180; tick++)
            runtime.Tick();

        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot landed));
        Assert.Equal(438f, landed.PositionY, 4);
        Assert.Equal(0f, landed.VelocityY, 4);

        ulong revision = landed.Revision.Value;
        runtime.Tick();

        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot stillLanded));
        Assert.Equal(438f, stillLanded.PositionY, 4);
        Assert.Equal(0f, stillLanded.VelocityY, 4);
        Assert.Equal(revision, stillLanded.Revision.Value);
    }

    private static WorldTileStore CreateGroundedWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 18; x <= 23; x++)
        {
            tiles.Set(x, 30, new WorldTile
            {
                Type = 1,
                Flags = WorldTileFlags.Active
            });
        }

        return tiles;
    }
}
