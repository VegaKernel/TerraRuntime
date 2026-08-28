using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimePlayerGenerationTests
{
    [Fact]
    public void Stale_commands_cannot_mutate_a_new_session_reusing_the_same_source_and_slot()
    {
        var slots = new PlayerSlotPool(1);
        var source = GameCommandSourceId.FromConnection(1);
        var state = new ServerRuntimeState();

        using PlayerJoinSession first = CreateAwaitingSpawnSession(slots);
        ConnectionHandle stale = Spawn(state, source, first);
        Assert.True(state.TryCapturePlayerSnapshot(stale.Player, out PlayerStateSnapshot firstSnapshot));
        Assert.Equal(new PlayerStateRevision(1), firstSnapshot.Revision);
        Assert.Equal(1600f, firstSnapshot.PositionX);
        Assert.Equal(3200f, firstSnapshot.PositionY);
        state.Apply(new PlayerDisconnectRuntimeCommand(stale));
        Assert.Equal(1, state.DisconnectedPlayers);
        Assert.False(state.TryCapturePlayerSnapshot(stale.Player, out _));
        first.Dispose();

        using PlayerJoinSession second = CreateAwaitingSpawnSession(slots);
        ConnectionHandle current = Spawn(state, source, second);
        Assert.Equal(stale.Player.Slot, current.Player.Slot);
        Assert.NotEqual(stale.Player.Generation, current.Player.Generation);

        PlayerAppearanceCommitRequest appearance = CreateAppearance(stale.Player.Slot);
        PlayerEquipmentCommitRequest equipment = CreateEquipment(stale.Player.Slot);
        PlayerMovementCommitRequest movement = CreateMovement(stale.Player.Slot);
        state.Apply(new PlayerAppearanceRuntimeCommand(stale, appearance));
        state.Apply(new PlayerEquipmentRuntimeCommand(stale, equipment));
        state.Apply(new PlayerMovementRuntimeCommand(stale, movement));
        state.Apply(new PlayerDisconnectRuntimeCommand(stale));

        Assert.Equal(1, state.RejectedPlayerAppearances);
        Assert.Equal(1, state.RejectedPlayerEquipmentUpdates);
        Assert.Equal(1, state.RejectedPlayerMovements);
        Assert.Equal(1, state.DisconnectedPlayers);

        state.Apply(new PlayerMovementRuntimeCommand(current, movement));
        Assert.Equal(1, state.AppliedPlayerMovements);
        Assert.True(state.TryCapturePlayerSnapshot(current.Player, out PlayerStateSnapshot currentSnapshot));
        Assert.Equal(new PlayerStateRevision(2), currentSnapshot.Revision);
        Assert.Equal(movement.PositionX, currentSnapshot.PositionX);
        Assert.Equal(movement.PositionY, currentSnapshot.PositionY);
        Assert.False(state.TryCapturePlayerSnapshot(stale.Player, out _));
    }

    [Fact]
    public void Explicit_player_state_revision_must_be_non_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerStateRevision(0));
        Assert.False(default(PlayerStateRevision).IsAssigned);
    }

    [Fact]
    public void Every_active_player_state_update_advances_the_snapshot_revision()
    {
        var slots = new PlayerSlotPool(1);
        var state = new ServerRuntimeState();
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        ConnectionHandle connection = Spawn(state, GameCommandSourceId.FromConnection(8), session);

        PlayerAppearanceCommitRequest appearance = CreateAppearance(session.Slot);
        state.Apply(new PlayerAppearanceRuntimeCommand(connection, appearance));
        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot afterAppearance));
        Assert.Equal(new PlayerStateRevision(2), afterAppearance.Revision);

        PlayerEquipmentCommitRequest equipment = CreateEquipment(session.Slot);
        state.Apply(new PlayerEquipmentRuntimeCommand(connection, equipment));
        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot afterEquipment));
        Assert.Equal(new PlayerStateRevision(3), afterEquipment.Revision);

        PlayerMovementCommitRequest movement = CreateMovement(session.Slot);
        state.Apply(new PlayerMovementRuntimeCommand(connection, movement));
        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot afterMovement));
        Assert.Equal(new PlayerStateRevision(4), afterMovement.Revision);
    }

    [Fact]
    public void Invalid_spawn_data_cannot_commit_the_join_session()
    {
        var slots = new PlayerSlotPool(1);
        var state = new ServerRuntimeState();
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9), session.Handle);
        PlayerSpawnCommitRequest request = CreateSpawn(session.Slot) with { Team = 6 };

        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));

        Assert.Equal(PlayerSpawnCommitResult.InvalidSpawnData, state.LastSpawnCommitResult);
        Assert.Equal(PlayerJoinState.AwaitingSpawn, session.State);
        Assert.Equal(0, state.CommittedPlayerSpawns);
    }

    private static ConnectionHandle Spawn(
        ServerRuntimeState state,
        GameCommandSourceId source,
        PlayerJoinSession session)
    {
        var connection = new ConnectionHandle(source, session.Handle);
        PlayerSpawnCommitRequest request = CreateSpawn(session.Slot);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);
        return connection;
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private static PlayerSpawnCommitRequest CreateSpawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);

    private static PlayerAppearanceCommitRequest CreateAppearance(PlayerSlotId slot) =>
        new(
            slot,
            0,
            0,
            0f,
            0,
            "player",
            0,
            0,
            0,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            0,
            0,
            0);

    private static PlayerEquipmentCommitRequest CreateEquipment(PlayerSlotId slot) =>
        new(slot, 0, 1, 0, 1, 0);

    private static PlayerMovementCommitRequest CreateMovement(PlayerSlotId slot) =>
        new(
            slot,
            0,
            0,
            0,
            0,
            0,
            1600f,
            3200f,
            false,
            0f,
            0f,
            false,
            0,
            false,
            0f,
            0f,
            0f,
            0f,
            false,
            0f,
            0f);
}
