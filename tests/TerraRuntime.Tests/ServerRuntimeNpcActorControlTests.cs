using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeNpcActorControlTests
{
    [Fact]
    public async Task Actor_intent_is_published_at_tick_boundary_and_then_flows_through_world_motion()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var setup = CreateStateWithServerPlayerTarget(tiles, targetX: 300f, targetY: 100f);
        using var targetPlayer = setup.Target;
        ServerRuntimeState state = setup.State;
        var spawnCompletion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new NpcStateUpdate(
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 0
            });

        state.Apply(new NpcSpawnRuntimeCommand(5, spawn, spawnCompletion));
        NpcSnapshot created = Assert.IsType<NpcSnapshot>(await spawnCompletion.Task);
        var controllerId = new ActorControllerId("test:runtime-actor");

        var acquireCompletion = new TaskCompletionSource<NpcActorAcquireStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorAcquireRuntimeCommand(created.Handle, controllerId, acquireCompletion));
        Assert.Equal(NpcActorAcquireStatus.Acquired, await acquireCompletion.Task);

        var intentCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorSetIntentRuntimeCommand(
            created.Handle,
            controllerId,
            NpcActorIntent.MoveTo(300f, 100f),
            intentCompletion));
        Assert.True(await intentCompletion.Task);

        Assert.True(state.TryCaptureNpcSnapshot(created.Handle, out NpcSnapshot beforeTick));
        Assert.Equal(100f, beforeTick.PositionX);
        Assert.Equal(0f, beforeTick.VelocityX);

        state.Tick();

        Assert.True(state.TryCaptureNpcSnapshot(created.Handle, out NpcSnapshot afterTick));
        Assert.True(afterTick.PositionX > beforeTick.PositionX);
        Assert.True(afterTick.VelocityX > 0f);
        Assert.True(afterTick.PositionY > beforeTick.PositionY);
        Assert.False(afterTick.Simulation.NoGravity);
    }

    [Fact]
    public async Task Release_controller_stages_all_owned_leases_and_restores_vanilla_fallback_next_tick()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var setup = CreateStateWithServerPlayerTarget(tiles, targetX: 200f, targetY: 100f);
        using var targetPlayer = setup.Target;
        ServerRuntimeState state = setup.State;
        var spawnCompletion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new NpcStateUpdate(
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { DirectionX = 1 });
        state.Apply(new NpcSpawnRuntimeCommand(6, spawn, spawnCompletion));
        NpcSnapshot created = Assert.IsType<NpcSnapshot>(await spawnCompletion.Task);
        var controllerId = new ActorControllerId("test:unload");

        var acquire = new TaskCompletionSource<NpcActorAcquireStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorAcquireRuntimeCommand(created.Handle, controllerId, acquire));
        Assert.Equal(NpcActorAcquireStatus.Acquired, await acquire.Task);

        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorSetIntentRuntimeCommand(created.Handle, controllerId, NpcActorIntent.Stop(), stop));
        Assert.True(await stop.Task);
        state.Tick();

        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorReleaseControllerRuntimeCommand(controllerId, release));
        Assert.Equal(1, await release.Task);
        state.Tick();

        var reacquire = new TaskCompletionSource<NpcActorAcquireStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorAcquireRuntimeCommand(created.Handle, controllerId, reacquire));
        Assert.Equal(NpcActorAcquireStatus.Acquired, await reacquire.Task);
    }

    [Fact]
    public async Task Follow_player_resolves_connection_free_server_owned_player_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var setup = CreateStateWithServerPlayerTarget(tiles, targetX: 300f, targetY: 100f);
        using RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease fakePlayer = setup.Target;
        ServerRuntimeState state = setup.State;

        var snapshotCompletion = new TaskCompletionSource<PlayerStateSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new PlayerStateSnapshotRuntimeCommand(fakePlayer.Player, snapshotCompletion));
        PlayerStateSnapshot? exposed = await snapshotCompletion.Task;
        Assert.True(exposed.HasValue);
        Assert.Equal(fakePlayer.Player, exposed.Value.Player);

        var spawnCompletion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new NpcStateUpdate(
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { DirectionX = 1 });
        state.Apply(new NpcSpawnRuntimeCommand(7, spawn, spawnCompletion));
        NpcSnapshot created = Assert.IsType<NpcSnapshot>(await spawnCompletion.Task);
        var controllerId = new ActorControllerId("test:follow-fake");

        var acquire = new TaskCompletionSource<NpcActorAcquireStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorAcquireRuntimeCommand(created.Handle, controllerId, acquire));
        Assert.Equal(NpcActorAcquireStatus.Acquired, await acquire.Task);

        var follow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorSetIntentRuntimeCommand(
            created.Handle,
            controllerId,
            NpcActorIntent.FollowPlayer(fakePlayer.Player),
            follow));
        Assert.True(await follow.Task);

        state.Tick();

        Assert.True(state.TryCaptureNpcSnapshot(created.Handle, out NpcSnapshot afterTick));
        Assert.True(afterTick.PositionX > 100f);
        Assert.True(afterTick.VelocityX > 0f);
        Assert.Equal(fakePlayer.Player.Slot.Value, afterTick.Target);
    }

    private static ServerPlayerTargetSetup CreateStateWithServerPlayerTarget(
        WorldTileStore tiles,
        float targetX,
        float targetY)
    {
        var slots = new PlayerSlotPool(8);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var serverPlayers = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var targetId = new ServerPlayerId("test:npc-target");

        Assert.Equal(
            ServerPlayerSlotAcquireResult.Acquired,
            identities.TryAcquire(targetId, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? acquired));
        RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease target =
            Assert.IsType<RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease>(acquired);
        Assert.True(serverPlayers.TrySpawn(targetId, targetX, targetY, out PlayerStateSnapshot snapshot));
        Assert.Equal(target.Player, snapshot.Player);

        return new ServerPlayerTargetSetup(
            new ServerRuntimeState(worldTiles: tiles, serverPlayerStates: serverPlayers),
            target);
    }

    private sealed record ServerPlayerTargetSetup(
        ServerRuntimeState State,
        RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease Target);
}
