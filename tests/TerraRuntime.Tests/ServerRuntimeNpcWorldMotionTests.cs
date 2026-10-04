using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeNpcWorldMotionTests
{
    [Fact]
    public async Task Authoritative_tick_publishes_one_final_targeting_ai_and_world_motion_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var commits = new RecordingCommits();
        var state = new ServerRuntimeState(npcs: new RuntimeNpcStore(commitSink: commits), worldTiles: tiles,
            worldClock: new RuntimeWorldClock(0, false, default, 0, 0));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(406), session.Handle);
        var playerSpawn = new PlayerSpawnCommitRequest(session.Slot, 20, 10, 0, 0, 0, 0, 0);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, playerSpawn));
        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);
        var completion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new NpcStateUpdate(
            Type: 2,
            NetId: 2,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: session.Slot.Value,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = -1
            });

        state.Apply(new NpcSpawnRuntimeCommand(5, spawn, completion));
        NpcSnapshot? createdValue = await completion.Task;
        Assert.True(createdValue.HasValue);
        NpcSnapshot created = createdValue.Value;
        commits.States.Clear();

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(created.Handle, out NpcSnapshot updated));
        // Retained admission and final physics each advance an unpublished revision; peers see one final state.
        Assert.Equal(new NpcRevision(created.Revision.Value + 2), updated.Revision);
        Assert.Equal(updated, Assert.Single(commits.States));
        Assert.Equal(100.1f, updated.PositionX, 5);
        Assert.Equal(199.96f, updated.PositionY, 5);
        Assert.Equal(0.1f, updated.VelocityX, 5);
        Assert.Equal(-0.04f, updated.VelocityY, 5);
        Assert.True(updated.Simulation.NoGravity);
        Assert.False(updated.Simulation.CollideX);
        Assert.False(updated.Simulation.CollideY);
    }

    private sealed class RecordingCommits : INpcStateCommitSink
    {
        public List<NpcSnapshot> States { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => States.Add(snapshot);
    }
}
