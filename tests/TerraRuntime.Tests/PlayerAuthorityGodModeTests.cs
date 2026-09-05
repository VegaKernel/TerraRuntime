using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class PlayerAuthorityGodModeTests
{
    [Fact]
    public async Task Godmode_mirrors_vanilla_creative_power_and_avoids_server_damage_without_repair_fallbacks()
    {
        var events = new RecordingPlayerEvents();
        var authority = new PlayerAuthority(events, worldTiles: null);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(71), session.Handle);

        var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
        Assert.True(authority.TryApply(new PlayerHealthRuntimeCommand(
            connection,
            new PlayerHealthCommitRequest(session.Slot, Life: 100, MaxLife: 100))));

        PlayerMovementCommitRequest movement = Movement(session.Slot, 1_620f, 3_180f, 1.25f, -0.5f);
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, movement)));

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(authority.TryApply(new SetPlayerGodModeRuntimeCommand(connection.Player, Enabled: true, completion)));
        Assert.True(await completion.Task);
        Assert.Equal([(connection.Player, true)], events.GodModeChanges);
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

        PlayerDamageCommitResult projectileResult = authority.TryCommitAuthoritativeNpcProjectileDamage(
            tick: 124,
            new NpcHandle(1, new NpcGeneration(1)),
            new ProjectileHandle(7, new ProjectileGeneration(1)),
            connection.Player,
            damage: 41,
            hitDirection: -1,
            VanillaPlayerImmunityChannel1458.General,
            out _);

        Assert.Equal(PlayerDamageCommitResult.AvoidedByGodMode, projectileResult);
        Assert.Equal(2, events.DamageAvoided);
        Assert.Empty(events.AuthoritativeHealthCorrections);
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot after));
        Assert.Equal(before, after);

        // Setting the same vanilla power state again is idempotent and does not spam net-module sync.
        var repeated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(authority.TryApply(new SetPlayerGodModeRuntimeCommand(connection.Player, Enabled: true, repeated)));
        Assert.True(await repeated.Task);
        Assert.Single(events.GodModeChanges);

        var disable = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(authority.TryApply(new SetPlayerGodModeRuntimeCommand(connection.Player, Enabled: false, disable)));
        Assert.True(await disable.Task);
        Assert.Equal([(connection.Player, true), (connection.Player, false)], events.GodModeChanges);
    }

    [Fact]
    public async Task Godmode_does_not_install_packet16_or_packet13_correction_fallbacks()
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
        PlayerMovementCommitRequest baseline = Movement(session.Slot, 1_600f, 3_200f, 0f, 0f);
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, baseline)));

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(authority.TryApply(new SetPlayerGodModeRuntimeCommand(connection.Player, Enabled: true, completion)));
        Assert.True(await completion.Task);

        // Vanilla Creative Godmode prevents these local Hurt mutations on the actual client. If a packet is
        // nevertheless submitted, TerraRuntime treats it normally instead of hiding a second repair/fallback path.
        Assert.True(authority.TryApply(new PlayerHealthRuntimeCommand(
            connection,
            new PlayerHealthCommitRequest(session.Slot, Life: 75, MaxLife: 100))));
        PlayerMovementCommitRequest submitted = Movement(session.Slot, 1_608f, 3_196f, 6f, -4f);
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, submitted)));

        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot state));
        Assert.Equal((short)75, state.Life);
        Assert.Equal(submitted.PositionX, state.PositionX);
        Assert.Equal(submitted.PositionY, state.PositionY);
        Assert.Equal(submitted.VelocityX, state.VelocityX);
        Assert.Equal(submitted.VelocityY, state.VelocityY);
        Assert.Empty(events.AuthoritativeHealthCorrections);
    }

    private static PlayerMovementCommitRequest Movement(
        PlayerSlotId slot,
        float x,
        float y,
        float velocityX,
        float velocityY) =>
        new(
            slot,
            ControlFlags: 0,
            MovementFlags: velocityX != 0f || velocityY != 0f
                ? VanillaPlayerMovementNormalizer.MovementVelocityPresentFlag
                : (byte)0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: x,
            PositionY: y,
            HasVelocity: velocityX != 0f || velocityY != 0f,
            VelocityX: velocityX,
            VelocityY: velocityY,
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
        public List<(PlayerHandle Player, bool Enabled)> GodModeChanges { get; } = [];
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

        public void PlayerDamageAvoided(PlayerHandle player, float positionX, float positionY, string text) =>
            DamageAvoided++;

        public void PlayerGodModeChanged(PlayerHandle player, bool enabled) =>
            GodModeChanges.Add((player, enabled));

        public void PlayerDisconnected(ConnectionHandle connection)
        {
        }
    }
}
