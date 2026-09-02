using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimePlayerVitalsTests
{
    [Fact]
    public void Pre_spawn_vitals_are_committed_into_the_first_authoritative_snapshot()
    {
        var slots = new PlayerSlotPool(1);
        var state = new ServerRuntimeState();
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), session.Handle);

        var health = new PlayerHealthCommitRequest(session.Slot, Life: 0, MaxLife: 1);
        var mana = new PlayerManaCommitRequest(session.Slot, Mana: 37, MaxMana: 80);
        state.Apply(new PlayerHealthRuntimeCommand(connection, health));
        state.Apply(new PlayerManaRuntimeCommand(connection, mana));

        Assert.Equal(1, state.AppliedPlayerHealthUpdates);
        Assert.Equal(1, state.AppliedPlayerManaUpdates);
        Assert.False(state.TryCapturePlayerSnapshot(connection.Player, out _));

        PlayerSpawnCommitRequest spawn = CreateSpawn(session.Slot);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));

        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot snapshot));
        Assert.Equal(new PlayerStateRevision(1), snapshot.Revision);
        Assert.True(snapshot.HasHealth);
        Assert.Equal((short)0, snapshot.Life);
        Assert.Equal((short)20, snapshot.MaxLife);
        Assert.True(snapshot.IsDead);
        Assert.True(snapshot.HasMana);
        Assert.Equal((short)37, snapshot.Mana);
        Assert.Equal((short)80, snapshot.MaxMana);
    }

    [Fact]
    public void Pending_vitals_from_an_old_generation_do_not_survive_slot_reuse()
    {
        var slots = new PlayerSlotPool(1);
        var state = new ServerRuntimeState();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(2);

        PlayerHandle staleHandle;
        using (PlayerJoinSession first = CreateAwaitingSpawnSession(slots))
        {
            var stale = new ConnectionHandle(source, first.Handle);
            staleHandle = stale.Player;
            var health = new PlayerHealthCommitRequest(first.Slot, Life: 50, MaxLife: 100);
            var mana = new PlayerManaCommitRequest(first.Slot, Mana: 20, MaxMana: 40);
            state.Apply(new PlayerHealthRuntimeCommand(stale, health));
            state.Apply(new PlayerManaRuntimeCommand(stale, mana));
        }

        using PlayerJoinSession second = CreateAwaitingSpawnSession(slots);
        var current = new ConnectionHandle(source, second.Handle);
        Assert.Equal(staleHandle.Slot, current.Player.Slot);
        Assert.NotEqual(staleHandle.Generation, current.Player.Generation);

        PlayerSpawnCommitRequest spawn = CreateSpawn(second.Slot);
        state.Apply(new PlayerSpawnRuntimeCommand(current, second, spawn));

        Assert.True(state.TryCapturePlayerSnapshot(current.Player, out PlayerStateSnapshot snapshot));
        Assert.False(snapshot.HasHealth);
        Assert.False(snapshot.HasMana);
        Assert.Equal((short)0, snapshot.Life);
        Assert.Equal((short)0, snapshot.Mana);
    }

    [Fact]
    public void Active_vitals_updates_advance_revision_and_reject_stale_generation()
    {
        var slots = new PlayerSlotPool(1);
        var state = new ServerRuntimeState();
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(3);
        var connection = new ConnectionHandle(source, session.Handle);
        PlayerSpawnCommitRequest spawn = CreateSpawn(session.Slot);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));

        var health = new PlayerHealthCommitRequest(session.Slot, Life: 90, MaxLife: 100);
        var mana = new PlayerManaCommitRequest(session.Slot, Mana: 60, MaxMana: 80);
        state.Apply(new PlayerHealthRuntimeCommand(connection, health));
        state.Apply(new PlayerManaRuntimeCommand(connection, mana));

        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot snapshot));
        Assert.Equal(new PlayerStateRevision(3), snapshot.Revision);
        Assert.Equal((short)90, snapshot.Life);
        Assert.Equal((short)60, snapshot.Mana);

        var stale = new ConnectionHandle(
            source,
            new PlayerHandle(
                session.Slot,
                new PlayerSessionGeneration(checked(session.Handle.Generation.Value + 1))));
        state.Apply(new PlayerHealthRuntimeCommand(stale, health));
        state.Apply(new PlayerManaRuntimeCommand(stale, mana));

        Assert.Equal(1, state.RejectedPlayerHealthUpdates);
        Assert.Equal(1, state.RejectedPlayerManaUpdates);
        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot unchanged));
        Assert.Equal(new PlayerStateRevision(3), unchanged.Revision);
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private static PlayerSpawnCommitRequest CreateSpawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);
}
