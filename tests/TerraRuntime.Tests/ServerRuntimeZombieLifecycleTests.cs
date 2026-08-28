using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeZombieLifecycleTests
{
    [Fact]
    public void World_backed_zombie_without_active_players_despawns_after_authoritative_tick()
    {
        var state = new ServerRuntimeState(worldTiles: CreateWorld());
        NpcSnapshot zombie = SpawnZombie(state, positionX: 100f, positionY: 100f);

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), state.LastNpcAiTick);
        Assert.False(state.TryCaptureNpcSnapshot(zombie.Handle, out _));
        Assert.Equal(1, state.AppliedNpcDespawns);
    }

    [Fact]
    public void Nearby_player_resets_daytime_encouraged_despawn_after_zombie_motion()
    {
        var state = new ServerRuntimeState(worldTiles: CreateWorld());
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession player = CreateAwaitingSpawnSession(slots);
        SpawnPlayer(
            state,
            GameCommandSourceId.FromConnection(42),
            player,
            spawnX: 20,
            spawnY: 10);
        NpcSnapshot zombie = SpawnZombie(state, positionX: 300f, positionY: 160f);

        state.Tick();

        Assert.True(state.TryCaptureNpcSnapshot(zombie.Handle, out NpcSnapshot updated));
        Assert.Equal(new NpcRevision(2), updated.Revision);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft - 1, updated.Simulation.TimeLeft);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, updated.Target);
        Assert.Equal(0, state.AppliedNpcDespawns);
    }

    [Fact]
    public void Distant_active_player_allows_daytime_encouraged_lifetime_to_count_down()
    {
        var state = new ServerRuntimeState(worldTiles: CreateWorld());
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession player = CreateAwaitingSpawnSession(slots);
        SpawnPlayer(
            state,
            GameCommandSourceId.FromConnection(43),
            player,
            spawnX: 150,
            spawnY: 10);
        NpcSnapshot zombie = SpawnZombie(state, positionX: 100f, positionY: 160f);

        state.Tick();

        Assert.True(state.TryCaptureNpcSnapshot(zombie.Handle, out NpcSnapshot updated));
        Assert.Equal(9, updated.Simulation.TimeLeft);
        Assert.Equal(0, state.AppliedNpcDespawns);
    }

    private static NpcSnapshot SpawnZombie(
        ServerRuntimeState state,
        float positionX,
        float positionY)
    {
        var completion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var update = new NpcStateUpdate(
            Type: 3,
            NetId: 3,
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1
            });
        state.Apply(new NpcSpawnRuntimeCommand(5, update, completion));
        NpcSnapshot? snapshot = completion.Task.GetAwaiter().GetResult();
        Assert.True(snapshot.HasValue);
        return snapshot.Value;
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private static void SpawnPlayer(
        ServerRuntimeState state,
        GameCommandSourceId source,
        PlayerJoinSession session,
        short spawnX,
        short spawnY)
    {
        var connection = new ConnectionHandle(source, session.Handle);
        var request = new PlayerSpawnCommitRequest(session.Slot, spawnX, spawnY, 0, 0, 0, 0, 0);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(4200, 1200));
}
