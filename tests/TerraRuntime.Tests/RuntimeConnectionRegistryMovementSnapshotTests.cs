using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeConnectionRegistryMovementSnapshotTests
{
    [Fact]
    public void Latest_authoritative_movement_frame_is_cached_by_player_slot()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(42);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));
        var slot = new PlayerSlotId(7);
        ConnectionHandle connection = Connection(source, slot);

        Assert.True(registry.TryRegister(source, outbound));
        PlayerSpawnCommitRequest spawn = CreateSpawnRequest(slot);
        registry.PlayerSpawned(connection, in spawn);
        Assert.False(registry.TryGetLatestPlayerMovementFrame(slot, out _));

        PlayerMovementCommitRequest first = CreateMovementRequest(slot, 100f, 200f, selectedItem: 3);
        registry.PlayerMoved(connection, in first);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame firstFrame));
        Assert.True(firstFrame.Bytes.Span.SequenceEqual(Encode(in first)));

        PlayerMovementCommitRequest second = CreateMovementRequest(slot, 300f, 400f, selectedItem: 9);
        registry.PlayerMoved(connection, in second);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame secondFrame));
        Assert.True(secondFrame.Bytes.Span.SequenceEqual(Encode(in second)));
        Assert.False(secondFrame.Bytes.Span.SequenceEqual(firstFrame.Bytes.Span));

        Assert.True(registry.TryUnregister(source, out PlayerHandle? playingPlayer));
        Assert.Equal(connection.Player, playingPlayer);
        Assert.False(registry.TryGetLatestPlayerMovementFrame(slot, out _));
    }

    [Fact]
    public void Respawn_invalidates_predeath_movement_baseline_until_new_packet_13_arrives()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(43);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));
        var slot = new PlayerSlotId(9);
        ConnectionHandle connection = Connection(source, slot);

        Assert.True(registry.TryRegister(source, outbound));
        PlayerSpawnCommitRequest spawn = CreateSpawnRequest(slot);
        registry.PlayerSpawned(connection, in spawn);
        PlayerMovementCommitRequest beforeDeath = CreateMovementRequest(slot, 900f, 1_200f, selectedItem: 2);
        registry.PlayerMoved(connection, in beforeDeath);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out _));

        var respawn = new PlayerSpawnCommitRequest(slot, SpawnX: 120, SpawnY: 210, RespawnTimer: 0, DeathsPve: 1, DeathsPvp: 0, Team: 0, SpawnContext: 0);
        registry.PlayerRespawned(connection, in respawn);

        Assert.False(registry.TryGetLatestPlayerMovementFrame(slot, out _));

        PlayerMovementCommitRequest afterRespawn = CreateMovementRequest(slot, 1_920f, 3_360f, selectedItem: 2);
        registry.PlayerMoved(connection, in afterRespawn);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame retained));
        Assert.True(retained.Bytes.Span.SequenceEqual(Encode(in afterRespawn)));
    }

    [Fact]
    public void Teleport_invalidates_preteleport_movement_baseline_until_new_packet_13_arrives()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(44);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));
        var slot = new PlayerSlotId(10);
        ConnectionHandle connection = Connection(source, slot);

        Assert.True(registry.TryRegister(source, outbound));
        PlayerSpawnCommitRequest spawn = CreateSpawnRequest(slot);
        registry.PlayerSpawned(connection, in spawn);
        PlayerMovementCommitRequest beforeTeleport = CreateMovementRequest(slot, 1_000f, 2_000f, selectedItem: 2);
        registry.PlayerMoved(connection, in beforeTeleport);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out _));

        registry.PlayerTeleported(connection, positionX: 4_000f, positionY: 5_000f, style: 1, failed: false);

        Assert.False(registry.TryGetLatestPlayerMovementFrame(slot, out _));
    }

    [Fact]
    public void Movement_from_non_owner_source_does_not_replace_cached_snapshot()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId owner = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId attacker = GameCommandSourceId.FromConnection(2);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));
        var slot = new PlayerSlotId(8);
        ConnectionHandle ownerConnection = Connection(owner, slot);

        Assert.True(registry.TryRegister(owner, outbound));
        PlayerSpawnCommitRequest spawn = CreateSpawnRequest(slot);
        registry.PlayerSpawned(ownerConnection, in spawn);

        PlayerMovementCommitRequest authoritative = CreateMovementRequest(slot, 10f, 20f, selectedItem: 1);
        registry.PlayerMoved(ownerConnection, in authoritative);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame before));

        PlayerMovementCommitRequest forged = CreateMovementRequest(slot, 999f, 999f, selectedItem: 50);
        registry.PlayerMoved(Connection(attacker, slot), in forged);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame after));
        Assert.True(after.Bytes.Span.SequenceEqual(before.Bytes.Span));
    }

    [Fact]
    public void Identical_playing_movement_update_is_not_relayed_twice_when_aoi_is_unchanged()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(51);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(52);
        var firstOutbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        var secondOutbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        var first = new PlayerSlotId(7);
        var second = new PlayerSlotId(8);
        ConnectionHandle firstConnection = Connection(firstSource, first);
        ConnectionHandle secondConnection = Connection(secondSource, second);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawnRequest(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawnRequest(second);
        registry.PlayerSpawned(firstConnection, in firstSpawn);
        registry.PlayerSpawned(secondConnection, in secondSpawn);

        PlayerMovementCommitRequest movement = CreateMovementRequest(first, 1_600f, 3_200f, selectedItem: 3);
        registry.PlayerMoved(firstConnection, in movement);
        int afterFirst = secondOutbound.QueuedFrames;
        registry.PlayerMoved(firstConnection, in movement);

        Assert.Equal(afterFirst, secondOutbound.QueuedFrames);
        Assert.Equal(1, registry.RelayedMovementFrames);
        Assert.Equal(1, registry.SuppressedDuplicateMovementFrames);
    }

    [Fact]
    public void Authoritative_movement_correction_is_sent_only_to_the_owning_client()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId ownerSource = GameCommandSourceId.FromConnection(61);
        GameCommandSourceId peerSource = GameCommandSourceId.FromConnection(62);
        var ownerOutbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 32_768, maxFrameBytes: 1_024));
        var peerOutbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 32_768, maxFrameBytes: 1_024));
        var ownerSlot = new PlayerSlotId(11);
        var peerSlot = new PlayerSlotId(12);
        ConnectionHandle owner = Connection(ownerSource, ownerSlot);
        ConnectionHandle peer = Connection(peerSource, peerSlot);

        Assert.True(registry.TryRegister(ownerSource, ownerOutbound));
        Assert.True(registry.TryRegister(peerSource, peerOutbound));
        PlayerSpawnCommitRequest ownerSpawn = CreateSpawnRequest(ownerSlot);
        PlayerSpawnCommitRequest peerSpawn = CreateSpawnRequest(peerSlot);
        registry.PlayerSpawned(owner, in ownerSpawn);
        registry.PlayerSpawned(peer, in peerSpawn);

        int ownerBefore = ownerOutbound.QueuedFrames;
        int peerBefore = peerOutbound.QueuedFrames;
        PlayerMovementCommitRequest movement = CreateMovementRequest(ownerSlot, 1_777f, 2_999f, selectedItem: 4);
        PlayerStateSnapshot correction = Snapshot(owner.Player, in movement);

        registry.PlayerAuthoritativeMovementCorrected(owner, in correction);

        Assert.Equal(ownerBefore + 1, ownerOutbound.QueuedFrames);
        Assert.Equal(peerBefore, peerOutbound.QueuedFrames);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(ownerSlot, out OutboundFrame retained));
        Assert.True(retained.Bytes.Span.SequenceEqual(Encode(in movement)));
    }

    private static PlayerStateSnapshot Snapshot(PlayerHandle player, in PlayerMovementCommitRequest movement) =>
        new(
            player,
            new PlayerStateRevision(1),
            Team: 0,
            movement.ControlFlags,
            movement.MovementFlags,
            movement.MiscFlags1,
            movement.MiscFlags2,
            movement.SelectedItem,
            movement.PositionX,
            movement.PositionY,
            movement.VelocityX,
            movement.VelocityY,
            movement.MountType,
            movement.PotionOfReturnOriginalPositionX,
            movement.PotionOfReturnOriginalPositionY,
            movement.PotionOfReturnHomePositionX,
            movement.PotionOfReturnHomePositionY,
            movement.CameraTargetX,
            movement.CameraTargetY);

    private static PlayerSpawnCommitRequest CreateSpawnRequest(PlayerSlotId slot) =>
        new(
            slot,
            SpawnX: 100,
            SpawnY: 200,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);

    private static ConnectionHandle Connection(GameCommandSourceId source, PlayerSlotId slot) =>
        new(source, new PlayerHandle(slot, new PlayerSessionGeneration(1)));

    private static PlayerMovementCommitRequest CreateMovementRequest(
        PlayerSlotId slot,
        float x,
        float y,
        byte selectedItem) =>
        new(
            slot,
            ControlFlags: 0x03,
            MovementFlags: 0x05,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: selectedItem,
            PositionX: x,
            PositionY: y,
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

    private static byte[] Encode(in PlayerMovementCommitRequest request)
    {
        var movement = new TerrariaPlayerMovementState(
            request.PlayerSlot.Value,
            request.ControlFlags,
            request.MovementFlags,
            request.MiscFlags1,
            request.MiscFlags2,
            request.SelectedItem,
            request.PositionX,
            request.PositionY,
            request.HasVelocity,
            request.VelocityX,
            request.VelocityY,
            request.HasMount,
            request.MountType,
            request.HasPotionOfReturnPositions,
            request.PotionOfReturnOriginalPositionX,
            request.PotionOfReturnOriginalPositionY,
            request.PotionOfReturnHomePositionX,
            request.PotionOfReturnHomePositionY,
            request.HasCameraTarget,
            request.CameraTargetX,
            request.CameraTargetY);

        return TerrariaPlayerMovementEncoder.Encode(in movement);
    }
}
