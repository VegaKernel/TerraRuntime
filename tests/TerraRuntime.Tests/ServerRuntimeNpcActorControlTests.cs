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
        var state = new ServerRuntimeState(worldTiles: tiles);
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
        var state = new ServerRuntimeState(worldTiles: tiles);
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
}
