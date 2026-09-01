using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeNpcAiTests
{
    [Fact]
    public async Task Authoritative_tick_runs_demon_eye_ai_over_runtime_owned_npc_store()
    {
        var state = new ServerRuntimeState();
        var completion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        NpcStateUpdate spawn = CreateDemonEye(
            simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = -1
            });

        state.Apply(new NpcSpawnRuntimeCommand(7, spawn, completion));

        Assert.Equal(1, state.AppliedNpcSpawns);
        Assert.Equal(0, state.RejectedNpcSpawns);
        NpcSnapshot? createdValue = await completion.Task;
        Assert.True(createdValue.HasValue);
        NpcSnapshot created = createdValue.Value;
        Assert.Equal(new NpcRevision(1), created.Revision);
        Assert.Equal(0f, created.VelocityX);
        Assert.Equal(0f, created.VelocityY);

        state.Tick();

        Assert.Equal(1, state.Updates);
        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(created.Handle, out NpcSnapshot updated));
        Assert.Equal(new NpcRevision(2), updated.Revision);
        Assert.Equal(0.1f, updated.VelocityX, 5);
        Assert.Equal(-0.04f, updated.VelocityY, 5);
        Assert.True(updated.Simulation.NoGravity);
    }

    [Fact]
    public void Authoritative_tick_targets_live_unmounted_player_before_demon_eye_motion()
    {
        var state = new ServerRuntimeState();
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession player = CreateAwaitingSpawnSession(slots);
        ConnectionHandle connection = SpawnPlayer(
            state,
            GameCommandSourceId.FromConnection(42),
            player,
            spawnX: 20,
            spawnY: 10);
        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot playerSnapshot));
        Assert.Equal(320f, playerSnapshot.PositionX);
        Assert.Equal(160f, playerSnapshot.PositionY);

        NpcSnapshot demonEye = Spawn(state, slot: 4, CreateDemonEye(NpcSimulationState.Initial));

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(demonEye.Handle, out NpcSnapshot updated));
        Assert.Equal((ushort)player.Slot.Value, updated.Target);
        Assert.Equal(1, updated.Simulation.DirectionX);
        Assert.Equal(-1, updated.Simulation.DirectionY);
        Assert.Equal(0.1f, updated.VelocityX, 5);
        Assert.Equal(-0.04f, updated.VelocityY, 5);
        Assert.Equal(new NpcRevision(2), updated.Revision);
    }

    [Fact]
    public async Task Unsupported_npc_type_is_examined_but_not_mutated_by_current_ai_phase()
    {
        var state = new ServerRuntimeState();
        var completion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        NpcStateUpdate spawn = new(
            Type: 1,
            NetId: 1,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

        state.Apply(new NpcSpawnRuntimeCommand(1, spawn, completion));
        NpcSnapshot? createdValue = await completion.Task;
        Assert.True(createdValue.HasValue);
        NpcSnapshot created = createdValue.Value;

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 0, 0, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(created.Handle, out NpcSnapshot unchanged));
        Assert.Equal(new NpcRevision(1), unchanged.Revision);
    }

    [Fact]
    public void Stale_npc_command_cannot_mutate_reused_runtime_slot()
    {
        var state = new ServerRuntimeState();
        NpcSnapshot original = Spawn(state, slot: 3, CreateDemonEye(NpcSimulationState.Initial));
        state.Apply(new NpcDespawnRuntimeCommand(original.Handle));
        Assert.Equal(1, state.AppliedNpcDespawns);

        NpcSnapshot replacement = Spawn(state, slot: 3, CreateDemonEye(NpcSimulationState.Initial));
        Assert.NotEqual(original.Handle, replacement.Handle);

        NpcStateUpdate staleUpdate = CreateDemonEye(NpcSimulationState.Initial) with
        {
            PositionX = 999f
        };
        state.Apply(new NpcUpdateRuntimeCommand(original.Handle, staleUpdate));
        state.Apply(new NpcDespawnRuntimeCommand(original.Handle));

        Assert.Equal(1, state.RejectedNpcUpdates);
        Assert.Equal(1, state.RejectedNpcDespawns);
        Assert.True(state.TryCaptureNpcSnapshot(replacement.Handle, out NpcSnapshot current));
        Assert.Equal(100f, current.PositionX);
        Assert.Equal(new NpcRevision(1), current.Revision);
    }

    private static NpcSnapshot Spawn(ServerRuntimeState state, byte slot, NpcStateUpdate update)
    {
        var completion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcSpawnRuntimeCommand(slot, update, completion));
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

    private static ConnectionHandle SpawnPlayer(
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
        return connection;
    }

    private static NpcStateUpdate CreateDemonEye(NpcSimulationState simulation) =>
        new(
            Type: 2,
            NetId: 2,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: simulation);
}
