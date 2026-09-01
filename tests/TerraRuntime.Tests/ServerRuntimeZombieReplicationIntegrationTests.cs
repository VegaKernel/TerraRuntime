using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeZombieReplicationIntegrationTests
{
    [Fact]
    public void Authoritative_Zombie_tick_replicates_packet23_to_playing_client()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var npcs = new RuntimeNpcStore(capacity: 16, commitSink: replication);
        var world = new WorldTileStore(new WorldDimensions(100, 100));
        var state = new ServerRuntimeState(replication, npcs: npcs, worldTiles: world);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(42);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 32_768, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));

        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession player = CreateAwaitingSpawnSession(slots);
        SpawnPlayer(state, source, player, spawnX: 10, spawnY: 10);

        NpcSnapshot zombie = SpawnZombie(state, slot: 3);
        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.RelayedFrames);

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(zombie.Handle, out NpcSnapshot updated));
        Assert.Equal(new NpcRevision(2), updated.Revision);
        Assert.Equal((ushort)player.Slot.Value, updated.Target);
        Assert.Equal(2, outbound.QueuedFrames);
        Assert.Equal(2, replication.RelayedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
        Assert.Equal(0, replication.RejectedFrames);
    }

    private static NpcSnapshot SpawnZombie(ServerRuntimeState state, byte slot)
    {
        var completion = new TaskCompletionSource<NpcSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        NpcStateUpdate spawn = new(
            Type: 3,
            NetId: 3,
            PositionX: 100f,
            // Keep this integration target below the fallback world-surface line. Daytime surface Zombies
            // intentionally discourage pursuit and retain the vanilla no-target sentinel; that behavior has
            // dedicated tests and is not what this packet-23 replication test is exercising.
            PositionY: 600f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1
            });

        state.Apply(new NpcSpawnRuntimeCommand(slot, spawn, completion));
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
}
