using TerraRuntime.Gameplay.Items;
using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class RuntimeConnectionRegistry
{
    internal bool TryGetLatestPlayerAppearanceFrame(PlayerSlotId slot, out OutboundFrame frame)
    {
        if (!TryGetPlayingEndpoint(slot, out RuntimeConnectionEndpoint endpoint))
        {
            frame = default;
            return false;
        }

        return endpoint.TryGetLatestAppearanceFrame(slot, out frame);
    }

    internal bool TryGetLatestPlayerMovementFrame(PlayerSlotId slot, out OutboundFrame frame)
    {
        if (!TryGetPlayingEndpoint(slot, out RuntimeConnectionEndpoint endpoint))
        {
            frame = default;
            return false;
        }

        return endpoint.TryGetLatestMovementFrame(out frame);
    }


    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
    {
        if (!VanillaPlayerAppearanceNormalizer.TryNormalize(in request, out PlayerAppearanceCommitRequest normalized))
            return;

        GameCommandSourceId source = connection.Source;
        if (connection.Player.Slot != normalized.PlayerSlot)
            return;

        if (!_endpoints.TryGetValue(source, out RuntimeConnectionEndpoint? endpoint))
            return;

        byte[] encoded = TerrariaPlayerReplicationFrameEncoder.EncodeAppearance(in normalized);
        endpoint.UpdateLatestAppearanceFrame(normalized.PlayerSlot, encoded);

        if (!endpoint.TryGetPlayingPlayer(out PlayerHandle currentPlayer) || currentPlayer != connection.Player)
            return;

        var frame = new OutboundFrame(encoded);
        foreach (KeyValuePair<GameCommandSourceId, RuntimeConnectionEndpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingSlot(out _))
                continue;

            if (pair.Value.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _relayedAppearanceFrames);
        }
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        GameCommandSourceId source = connection.Source;
        if (connection.Player.Slot != request.PlayerSlot ||
            !VanillaPlayerItemSlotCatalog.IsValid(request.SlotId) ||
            !VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId) ||
            !request.TryGetCanonicalItemType(out _))
            return;

        if (!_endpoints.TryGetValue(source, out RuntimeConnectionEndpoint? endpoint))
            return;

        byte[] encoded = TerrariaPlayerReplicationFrameEncoder.EncodeEquipment(in request);
        if (!endpoint.UpdateLatestEquipmentFrame(request.PlayerSlot, request.SlotId, encoded))
            Interlocked.Increment(ref _droppedEquipmentSnapshotUpdates);

        if (!endpoint.TryGetPlayingPlayer(out PlayerHandle currentPlayer) || currentPlayer != connection.Player)
            return;

        var frame = new OutboundFrame(encoded);
        foreach (KeyValuePair<GameCommandSourceId, RuntimeConnectionEndpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingSlot(out _))
                continue;

            if (pair.Value.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _relayedEquipmentFrames);
        }
    }

    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        GameCommandSourceId source = connection.Source;
        if (connection.Player.Slot != request.ClaimedSlot ||
            !VanillaPlayerSpawnValidator.IsValid(in request))
            return;

        if (!_endpoints.TryGetValue(source, out RuntimeConnectionEndpoint? endpoint))
            return;

        float positionX = request.SpawnX * 16f;
        float positionY = request.SpawnY * 16f;
        endpoint.MarkPlaying(connection.Player);
        endpoint.UpdatePosition(positionX, positionY);
        Interlocked.Exchange(ref _playingEndpoints[request.ClaimedSlot.Value], endpoint);
        _movementVisibilityReadiness.ClearPlayer(request.ClaimedSlot);

        SynchronizePlayerBaselines(source, request.ClaimedSlot, endpoint);
        SynchronizeServerPlayerBaselines(endpoint);

        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[MaxPlayerSlots];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[MaxPlayerSlots];
        RuntimePlayerVisibilityUpdate visibility = _interestRouter.TrackPlayer(
            request.ClaimedSlot,
            positionX,
            positionY,
            entered,
            left);
        ResetMovementVisibilityReadiness(request.ClaimedSlot, visibility, entered, left);
    }

    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
        if (!VanillaPlayerMovementNormalizer.TryNormalize(in request, out PlayerMovementCommitRequest normalized))
            return;

        GameCommandSourceId source = connection.Source;
        if (!_endpoints.TryGetValue(source, out RuntimeConnectionEndpoint? origin) ||
            !origin.TryGetPlayingPlayer(out PlayerHandle originPlayer) ||
            originPlayer != connection.Player ||
            originPlayer.Slot != normalized.PlayerSlot)
        {
            return;
        }

        PlayerSlotId originSlot = originPlayer.Slot;

        origin.UpdatePosition(normalized.PositionX, normalized.PositionY);

        // Cache the current authoritative movement before refreshing visibility. A future AOI-enter
        // resync can therefore use this exact movement as the subject's baseline in the same commit.
        byte[] encoded = TerrariaPlayerReplicationFrameEncoder.EncodeMovement(in normalized);
        origin.UpdateLatestMovementFrame(encoded);
        var frame = new OutboundFrame(encoded);

        // Track authoritative positions even while interest management is disabled. A live enable
        // can therefore start from current state instead of waiting for every player to move again.
        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[MaxPlayerSlots];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[MaxPlayerSlots];
        RuntimePlayerVisibilityUpdate visibility = _interestRouter.TrackPlayer(
            normalized.PlayerSlot,
            normalized.PositionX,
            normalized.PositionY,
            entered,
            left);
        ResetMovementVisibilityReadiness(normalized.PlayerSlot, visibility, entered, left);

        RuntimePlayerInterestState subject = origin.CreateInterestState(originSlot);

        foreach (KeyValuePair<GameCommandSourceId, RuntimeConnectionEndpoint> pair in _endpoints)
        {
            if (pair.Key == source ||
                !pair.Value.TryGetPlayingSlot(out PlayerSlotId observerSlot))
            {
                continue;
            }

            RuntimePlayerInterestState observer = pair.Value.CreateInterestState(observerSlot);
            if (!_interestRouter.ShouldRelayPlayerMovement(in observer, in subject))
                continue;

            if (pair.Value.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _relayedMovementFrames);
        }
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        GameCommandSourceId source = connection.Source;
        PlayerSlotId slot = connection.Player.Slot;
        _interestRouter.RemovePlayer(slot);
        _movementVisibilityReadiness.ClearPlayer(slot);

        var inactiveFrame = new OutboundFrame(
            TerrariaPlayerActiveEncoder.Encode(slot.Value, active: false));
        foreach (KeyValuePair<GameCommandSourceId, RuntimeConnectionEndpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingSlot(out _))
                continue;

            if (pair.Value.Outbound.TryEnqueue(inactiveFrame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _playerDeactivationFrames);
        }

        if (_endpoints.TryGetValue(source, out RuntimeConnectionEndpoint? endpoint))
        {
            Interlocked.CompareExchange(ref _playingEndpoints[slot.Value], null, endpoint);
            endpoint.ClearPlaying(connection.Player);
        }
    }

    private void SynchronizePlayerBaselines(
        GameCommandSourceId source,
        PlayerSlotId slot,
        RuntimeConnectionEndpoint endpoint)
    {
        var originActive = new OutboundFrame(
            TerrariaPlayerActiveEncoder.Encode(slot.Value, active: true));
        bool hasOriginAppearance = endpoint.TryGetLatestAppearanceFrame(slot, out OutboundFrame originAppearance);

        foreach (KeyValuePair<GameCommandSourceId, RuntimeConnectionEndpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingSlot(out PlayerSlotId peerSlot))
                continue;

            if (pair.Value.Outbound.TryEnqueue(originActive) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _playerActiveBaselineFrames);

            var peerActive = new OutboundFrame(
                TerrariaPlayerActiveEncoder.Encode(peerSlot.Value, active: true));
            if (endpoint.Outbound.TryEnqueue(peerActive) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _playerActiveBaselineFrames);

            if (hasOriginAppearance &&
                pair.Value.Outbound.TryEnqueue(originAppearance) == OutboundEnqueueResult.Enqueued)
            {
                Interlocked.Increment(ref _appearanceBaselineFrames);
            }

            if (pair.Value.TryGetLatestAppearanceFrame(peerSlot, out OutboundFrame peerAppearance) &&
                endpoint.Outbound.TryEnqueue(peerAppearance) == OutboundEnqueueResult.Enqueued)
            {
                Interlocked.Increment(ref _appearanceBaselineFrames);
            }

            Interlocked.Add(
                ref _equipmentBaselineFrames,
                endpoint.EnqueueEquipmentBaselineTo(pair.Value, slot));
            Interlocked.Add(
                ref _equipmentBaselineFrames,
                pair.Value.EnqueueEquipmentBaselineTo(endpoint, peerSlot));
        }
    }

}
