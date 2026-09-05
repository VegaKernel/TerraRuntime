using TerraRuntime.Gameplay.Items;
using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    // Terraria world coordinates use 16 pixels per tile. Keep this source-backed scale local to spawn projection.
    private const float VanillaTileSizePixels = 16f;

    internal bool TryGetLatestPlayerAppearanceFrame(PlayerSlotId slot, out OutboundFrame frame)
    {
        if (!TryGetPlayingEndpoint(slot, out RuntimeConnectionEndpoint endpoint))
        {
            frame = default;
            return false;
        }

        frame = default;
        return endpoint.TryGetPlayingPlayer(out PlayerHandle player) &&
            endpoint.TryGetLatestAppearanceFrame(player, out frame);
    }

    internal bool TryGetLatestPlayerMovementFrame(PlayerSlotId slot, out OutboundFrame frame)
    {
        if (!TryGetPlayingEndpoint(slot, out RuntimeConnectionEndpoint endpoint))
        {
            frame = default;
            return false;
        }

        frame = default;
        return endpoint.TryGetPlayingPlayer(out PlayerHandle player) &&
            endpoint.TryGetLatestMovementFrame(player, out frame);
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
        bool changed = endpoint.UpdateLatestAppearanceFrame(connection.Player, encoded);

        if (!endpoint.TryGetPlayingPlayer(out PlayerHandle currentPlayer) || currentPlayer != connection.Player)
            return;
        if (!changed)
        {
            Interlocked.Increment(ref _suppressedDuplicateAppearanceFrames);
            return;
        }

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
        bool retained = endpoint.UpdateLatestEquipmentFrame(
            connection.Player,
            request.SlotId,
            encoded,
            out bool changed);
        if (!retained)
            Interlocked.Increment(ref _droppedEquipmentSnapshotUpdates);

        if (!endpoint.TryGetPlayingPlayer(out PlayerHandle currentPlayer) || currentPlayer != connection.Player)
            return;
        if (retained && !changed)
        {
            Interlocked.Increment(ref _suppressedDuplicateEquipmentFrames);
            return;
        }

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

        float positionX = request.SpawnX * VanillaTileSizePixels;
        float positionY = request.SpawnY * VanillaTileSizePixels;
        endpoint.MarkPlaying(connection.Player);
        endpoint.UpdatePosition(positionX, positionY);
        Interlocked.Exchange(ref _playingEndpoints[request.ClaimedSlot.Value], endpoint);
        _movementVisibilityReadiness.ClearPlayer(request.ClaimedSlot);

        SynchronizePlayerBaselines(source, request.ClaimedSlot, endpoint);
        SynchronizeServerPlayerBaselines(endpoint);

        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[ProtocolPlayerSlotCount];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[ProtocolPlayerSlotCount];
        RuntimePlayerVisibilityUpdate visibility = _interestRouter.TrackPlayer(
            request.ClaimedSlot,
            positionX,
            positionY,
            entered,
            left);
        ResetMovementVisibilityReadiness(request.ClaimedSlot, visibility, entered, left);
    }

    public void PlayerRespawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        if (connection.Player.Slot != request.ClaimedSlot ||
            !VanillaPlayerSpawnValidator.IsValid(in request) ||
            !_endpoints.TryGetValue(connection.Source, out RuntimeConnectionEndpoint? endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle player) || player != connection.Player)
            return;

        float positionX = request.SpawnX * VanillaTileSizePixels;
        float positionY = request.SpawnY * VanillaTileSizePixels;
        // Respawn is a position discontinuity represented by packet 12. Any retained pre-death packet-13
        // baseline is now stale and must not be reused by a later AOI-enter resync.
        endpoint.ClearLatestMovementFrame(connection.Player);
        endpoint.UpdatePosition(positionX, positionY);
        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[ProtocolPlayerSlotCount];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[ProtocolPlayerSlotCount];
        RuntimePlayerVisibilityUpdate visibility = _interestRouter.TrackPlayer(
            request.ClaimedSlot, positionX, positionY, entered, left);
        ResetMovementVisibilityReadiness(request.ClaimedSlot, visibility, entered, left);
        byte[] encoded = TerrariaPlayerReplicationFrameEncoder.EncodeSpawn(in request);
        // Packet 12 originated from this client. Echoing the same spawn back to it can retrigger the
        // local spawn transition, causing visible flicker, input lock and a packet feedback loop.
        Interlocked.Add(ref _relayedMovementFrames, BroadcastToPlayingExcept(connection.Source, encoded));
    }

    public void PlayerTeleported(ConnectionHandle connection, float positionX, float positionY, byte style, bool failed)
    {
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) ||
            !_endpoints.TryGetValue(connection.Source, out RuntimeConnectionEndpoint? endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle player) || player != connection.Player)
            return;

        // Packet 65 establishes the discontinuous teleport position. Do not let an older retained packet 13
        // become a future AOI resync baseline and pull an observer back toward the pre-teleport position.
        endpoint.ClearLatestMovementFrame(player);
        endpoint.UpdatePosition(positionX, positionY);
        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[ProtocolPlayerSlotCount];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[ProtocolPlayerSlotCount];
        RuntimePlayerVisibilityUpdate visibility = _interestRouter.TrackPlayer(
            player.Slot, positionX, positionY, entered, left);
        ResetMovementVisibilityReadiness(player.Slot, visibility, entered, left);
        byte[] encoded = TerrariaPlayerReplicationFrameEncoder.EncodeTeleport(player.Slot, positionX, positionY, style, failed);
        Interlocked.Add(ref _relayedMovementFrames, BroadcastToPlaying(encoded));
    }

    public void PlayerAuthoritativeMovementCorrected(ConnectionHandle connection, in PlayerStateSnapshot player)
    {
        if (!connection.IsAssigned ||
            player.Player != connection.Player ||
            !_endpoints.TryGetValue(connection.Source, out RuntimeConnectionEndpoint? endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle currentPlayer) ||
            currentPlayer != connection.Player)
        {
            return;
        }

        byte[] encoded = TerrariaPlayerReplicationFrameEncoder.EncodeMovement(in player);
        endpoint.UpdatePosition(player.PositionX, player.PositionY);
        endpoint.UpdateLatestMovementFrame(player.Player, encoded);

        // This frame repairs client-local Hurt/knockback state for the owner. No authoritative movement
        // changed, so relaying it to peers would be duplicate traffic and could create visible jitter.
        _ = endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded));
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
        bool changed = origin.UpdateLatestMovementFrame(originPlayer, encoded);
        var frame = new OutboundFrame(encoded);

        // Track authoritative positions even while interest management is disabled. A live enable
        // can therefore start from current state instead of waiting for every player to move again.
        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[ProtocolPlayerSlotCount];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[ProtocolPlayerSlotCount];
        RuntimePlayerVisibilityUpdate visibility = _interestRouter.TrackPlayer(
            normalized.PlayerSlot,
            normalized.PositionX,
            normalized.PositionY,
            entered,
            left);
        ResetMovementVisibilityReadiness(normalized.PlayerSlot, visibility, entered, left);

        // Vanilla clients can repeat packet 13 with an identical payload while idle. Once AOI membership
        // is also unchanged, relaying that exact state again only burns every peer's outbound queue.
        if (!changed && visibility.Entered == 0 && visibility.Left == 0)
        {
            Interlocked.Increment(ref _suppressedDuplicateMovementFrames);
            return;
        }

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
        OutboundFrame originAppearance = default;
        bool hasOriginAppearance = endpoint.TryGetPlayingPlayer(out PlayerHandle originPlayer) &&
            endpoint.TryGetLatestAppearanceFrame(originPlayer, out originAppearance);

        foreach (KeyValuePair<GameCommandSourceId, RuntimeConnectionEndpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingPlayer(out PlayerHandle peerPlayer))
                continue;

            if (pair.Value.Outbound.TryEnqueue(originActive) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _playerActiveBaselineFrames);

            var peerActive = new OutboundFrame(
                TerrariaPlayerActiveEncoder.Encode(peerPlayer.Slot.Value, active: true));
            if (endpoint.Outbound.TryEnqueue(peerActive) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _playerActiveBaselineFrames);

            if (hasOriginAppearance &&
                pair.Value.Outbound.TryEnqueue(originAppearance) == OutboundEnqueueResult.Enqueued)
            {
                Interlocked.Increment(ref _appearanceBaselineFrames);
            }

            if (pair.Value.TryGetLatestAppearanceFrame(peerPlayer, out OutboundFrame peerAppearance) &&
                endpoint.Outbound.TryEnqueue(peerAppearance) == OutboundEnqueueResult.Enqueued)
            {
                Interlocked.Increment(ref _appearanceBaselineFrames);
            }

            Interlocked.Add(
                ref _equipmentBaselineFrames,
                endpoint.EnqueueEquipmentBaselineTo(pair.Value, originPlayer));
            Interlocked.Add(
                ref _equipmentBaselineFrames,
                pair.Value.EnqueueEquipmentBaselineTo(endpoint, peerPlayer));
        }
    }

}
