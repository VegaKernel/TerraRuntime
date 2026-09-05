using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class PlayerAuthorityGodModeTests
{
    [Fact]
    public void Godmode_avoidance_reasserts_owner_health_and_movement_without_mutating_authoritative_state()
    {
        var events = new RecordingPlayerEvents();
        var authority = new PlayerAuthority(events, worldTiles: null);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(71), session.Handle);

        var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));

        var health = new PlayerHealthCommitRequest(session.Slot, Life: 100, MaxLife: 100);
        Assert.True(authority.TryApply(new PlayerHealthRuntimeCommand(connection, health)));

        var movement = new PlayerMovementCommitRequest(
            session.Slot,
            ControlFlags: 0x03,
            MovementFlags: 0x05,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 2,
            PositionX: 1_620f,
            PositionY: 3_180f,
            HasVelocity: true,
            VelocityX: 1.25f,
            VelocityY: -0.5f,
            HasMount: false,
            MountType: 0,
            HasPotionOfReturnPositions: false,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            HasCameraTarget: false,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, movement)));

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(authority.TryApply(new SetPlayerGodModeRuntimeCommand(connection.Player, Enabled: true, completion)));
        Assert.True(completion.Task.GetAwaiter().GetResult());
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot before));

        authority.AdvanceCombatTick(123);
        PlayerDamageCommitResult result = authority.TryCommitAuthoritativeNpcContactDamage(
            tick: 123,
            new NpcHandle(1, new NpcGeneration(1)),
            connection.Player,
            damage: 37,
            hitDirection: 1,
            VanillaPlayerImmunityChannel1458.General,
            out _);

        Assert.Equal(PlayerDamageCommitResult.AvoidedByGodMode, result);
        Assert.Single(events.AuthoritativeHealthCorrections);
        Assert.Equal((short)100, events.AuthoritativeHealthCorrections[0].Life);
        Assert.Single(events.AuthoritativeMovementCorrections);
        Assert.Equal(before, events.AuthoritativeMovementCorrections[0]);
        Assert.Equal(1, events.DamageAvoided);

        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot after));
        Assert.Equal(before, after);

        // Packet 13 frames produced by the client's local Hurt must not smuggle knockback back into
        // authoritative state. Every frame inside the short correction epoch is answered with the baseline.
        var knocked = movement with
        {
            PositionX = movement.PositionX + 4.5f,
            PositionY = movement.PositionY - 3.5f,
            VelocityX = 4.5f,
            VelocityY = -3.5f
        };
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, knocked)));
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot afterKnockbackReport));
        Assert.Equal(before, afterKnockbackReport);
        Assert.Equal(2, events.AuthoritativeMovementCorrections.Count);

        var repeatedKnockback = knocked with { PositionX = knocked.PositionX + 2f };
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, repeatedKnockback)));
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot afterRepeatedKnockback));
        Assert.Equal(before, afterRepeatedKnockback);
        Assert.Equal(3, events.AuthoritativeMovementCorrections.Count);

        // The guard is short-lived: normal movement after the correction window is accepted.
        authority.AdvanceCombatTick(126);
        var resumed = movement with { PositionX = movement.PositionX + 12f, VelocityX = 2f };
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, resumed)));
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot resumedState));
        Assert.Equal(resumed.PositionX, resumedState.PositionX);
        Assert.Equal(resumed.VelocityX, resumedState.VelocityX);
    }

    [Fact]
    public void Godmode_rejects_client_local_health_loss_and_repairs_environmental_knockback_state()
    {
        var events = new RecordingPlayerEvents();
        var authority = new PlayerAuthority(events, worldTiles: null);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(72), session.Handle);

        var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
        Assert.True(authority.TryApply(new PlayerHealthRuntimeCommand(
            connection,
            new PlayerHealthCommitRequest(session.Slot, Life: 100, MaxLife: 100))));

        var movement = new PlayerMovementCommitRequest(
            session.Slot,
            ControlFlags: 0, MovementFlags: 0, MiscFlags1: 0, MiscFlags2: 0, SelectedItem: 0,
            PositionX: 1_600f, PositionY: 3_200f,
            HasVelocity: false, VelocityX: 0f, VelocityY: 0f,
            HasMount: false, MountType: 0,
            HasPotionOfReturnPositions: false,
            PotionOfReturnOriginalPositionX: 0f, PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f, PotionOfReturnHomePositionY: 0f,
            HasCameraTarget: false, CameraTargetX: 0f, CameraTargetY: 0f);
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, movement)));

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(authority.TryApply(new SetPlayerGodModeRuntimeCommand(connection.Player, Enabled: true, completion)));
        Assert.True(completion.Task.GetAwaiter().GetResult());

        authority.AdvanceCombatTick(200);
        Assert.True(authority.TryApply(new PlayerHealthRuntimeCommand(
            connection,
            new PlayerHealthCommitRequest(session.Slot, Life: 75, MaxLife: 100))));

        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot afterHealthReport));
        Assert.Equal((short)100, afterHealthReport.Life);
        Assert.Single(events.AuthoritativeHealthCorrections);
        Assert.Single(events.AuthoritativeMovementCorrections);

        var localKnockback = movement with
        {
            PositionX = movement.PositionX + 8f,
            VelocityX = 6f,
            VelocityY = -4f,
            HasVelocity = true
        };
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, localKnockback)));
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot afterMovementReport));
        Assert.Equal(movement.PositionX, afterMovementReport.PositionX);
        Assert.Equal(0f, afterMovementReport.VelocityX);
        Assert.Equal(0f, afterMovementReport.VelocityY);
        Assert.Equal(2, events.AuthoritativeMovementCorrections.Count);
        Assert.Equal(0, events.DamageAvoided);
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private sealed class RecordingPlayerEvents : IRuntimePlayerEventSink
    {
        public List<PlayerHealthCommitRequest> AuthoritativeHealthCorrections { get; } = [];
        public List<PlayerStateSnapshot> AuthoritativeMovementCorrections { get; } = [];
        public int DamageAvoided { get; private set; }

        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
        {
        }

        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        {
        }

        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
        {
        }

        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
        {
        }

        public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request) =>
            AuthoritativeHealthCorrections.Add(request);

        public void PlayerAuthoritativeMovementCorrected(ConnectionHandle connection, in PlayerStateSnapshot player) =>
            AuthoritativeMovementCorrections.Add(player);

        public void PlayerDamageAvoided(PlayerHandle player, float positionX, float positionY, string text) =>
            DamageAvoided++;

        public void PlayerDisconnected(ConnectionHandle connection)
        {
        }
    }
}
