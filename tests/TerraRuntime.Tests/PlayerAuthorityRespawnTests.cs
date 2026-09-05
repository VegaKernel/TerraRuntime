using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class PlayerAuthorityRespawnTests
{
    [Fact]
    public void Respawn_clears_predeath_transient_movement_state_and_keeps_selected_item()
    {
        var authority = new PlayerAuthority(events: null, worldTiles: null);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(73), session.Handle);

        var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));

        var movement = new PlayerMovementCommitRequest(
            session.Slot,
            ControlFlags: 0x7f, MovementFlags: 0x85, MiscFlags1: 0x40, MiscFlags2: 0x20, SelectedItem: 7,
            PositionX: 1_700f, PositionY: 3_300f,
            HasVelocity: true, VelocityX: 5f, VelocityY: -4f,
            HasMount: true, MountType: 2,
            HasPotionOfReturnPositions: true,
            PotionOfReturnOriginalPositionX: 1f, PotionOfReturnOriginalPositionY: 2f,
            PotionOfReturnHomePositionX: 3f, PotionOfReturnHomePositionY: 4f,
            HasCameraTarget: true, CameraTargetX: 5f, CameraTargetY: 6f);
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, movement)));
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot beforeRespawn));
        Assert.Equal((ushort)2, beforeRespawn.MountType);
        Assert.Equal(1f, beforeRespawn.PotionOfReturnOriginalPositionX);
        Assert.Equal(5f, beforeRespawn.CameraTargetX);

        var respawn = new PlayerSpawnCommitRequest(session.Slot, 120, 210, 0, 1, 0, 0, 0);
        Assert.True(authority.TryApply(new PlayerRespawnRuntimeCommand(connection, respawn)));
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot state));

        Assert.Equal(120 * 16f, state.PositionX);
        Assert.Equal(210 * 16f, state.PositionY);
        Assert.Equal((byte)0, state.ControlFlags);
        Assert.Equal((byte)0, state.MovementFlags);
        Assert.Equal((byte)0, state.MiscFlags1);
        Assert.Equal((byte)0, state.MiscFlags2);
        Assert.Equal((byte)7, state.SelectedItem);
        Assert.Equal(0f, state.VelocityX);
        Assert.Equal(0f, state.VelocityY);
        Assert.Equal((ushort)0, state.MountType);
        Assert.Equal(0f, state.PotionOfReturnOriginalPositionX);
        Assert.Equal(0f, state.PotionOfReturnOriginalPositionY);
        Assert.Equal(0f, state.PotionOfReturnHomePositionX);
        Assert.Equal(0f, state.PotionOfReturnHomePositionY);
        Assert.Equal(0f, state.CameraTargetX);
        Assert.Equal(0f, state.CameraTargetY);
        Assert.False(state.IsDead);
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }
}
