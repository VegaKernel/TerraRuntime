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

        Assert.True(registry.TryRegister(source, outbound));
        PlayerSpawnCommitRequest spawn = CreateSpawnRequest(slot);
        registry.PlayerSpawned(source, in spawn);
        Assert.False(registry.TryGetLatestPlayerMovementFrame(slot, out _));

        PlayerMovementCommitRequest first = CreateMovementRequest(slot, 100f, 200f, selectedItem: 3);
        registry.PlayerMoved(source, in first);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame firstFrame));
        Assert.True(firstFrame.Bytes.Span.SequenceEqual(Encode(in first)));

        PlayerMovementCommitRequest second = CreateMovementRequest(slot, 300f, 400f, selectedItem: 9);
        registry.PlayerMoved(source, in second);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame secondFrame));
        Assert.True(secondFrame.Bytes.Span.SequenceEqual(Encode(in second)));
        Assert.False(secondFrame.Bytes.Span.SequenceEqual(firstFrame.Bytes.Span));

        Assert.True(registry.TryUnregister(source, out PlayerSlotId? playingSlot));
        Assert.Equal(slot, playingSlot);
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

        Assert.True(registry.TryRegister(owner, outbound));
        PlayerSpawnCommitRequest spawn = CreateSpawnRequest(slot);
        registry.PlayerSpawned(owner, in spawn);

        PlayerMovementCommitRequest authoritative = CreateMovementRequest(slot, 10f, 20f, selectedItem: 1);
        registry.PlayerMoved(owner, in authoritative);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame before));

        PlayerMovementCommitRequest forged = CreateMovementRequest(slot, 999f, 999f, selectedItem: 50);
        registry.PlayerMoved(attacker, in forged);
        Assert.True(registry.TryGetLatestPlayerMovementFrame(slot, out OutboundFrame after));
        Assert.True(after.Bytes.Span.SequenceEqual(before.Bytes.Span));
    }

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

    private static PlayerMovementCommitRequest CreateMovementRequest(
        PlayerSlotId slot,
        float x,
        float y,
        byte selectedItem) =>
        new(
            slot,
            ControlFlags: 0x03,
            MovementFlags: 0x01,
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
