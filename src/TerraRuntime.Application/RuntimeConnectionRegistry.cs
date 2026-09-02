using TerraRuntime.Gameplay.Items;
using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Tracks live connection outbound queues independently from socket ownership and fans out
/// authoritative player events only to clients that have completed the spawn transition.
/// Recipient selection always passes through the runtime-owned interest router before enqueue.
/// </summary>
internal sealed class RuntimeConnectionRegistry : IRuntimePlayerEventSink, IRuntimeServerPlayerEventSink
{
    private const int MaxPlayerSlots = 256;
    private readonly ConcurrentDictionary<GameCommandSourceId, RuntimeConnectionEndpoint> _endpoints = new();
    private readonly RuntimeConnectionEndpoint?[] _playingEndpoints = new RuntimeConnectionEndpoint?[MaxPlayerSlots];
    private readonly ServerPlayerReplicaStore _serverPlayers = new();
    private readonly RuntimeInterestRouter _interestRouter;
    private readonly RuntimePlayerMovementVisibilityReadiness _movementVisibilityReadiness = new();
    private long _relayedAppearanceFrames;
    private long _appearanceBaselineFrames;
    private long _relayedEquipmentFrames;
    private long _equipmentBaselineFrames;
    private long _droppedEquipmentSnapshotUpdates;
    private long _playerActiveBaselineFrames;
    private long _playerDeactivationFrames;
    private long _relayedMovementFrames;
    private long _movementResyncFrames;
    private long _serverPlayerHealthFrames;
    private long _serverPlayerManaFrames;

    public RuntimeConnectionRegistry(
        IInterestManagementControl? interestManagement = null,
        WorldDimensions? dimensions = null)
    {
        _interestRouter = new RuntimeInterestRouter(
            interestManagement ?? new InterestManagementControl(),
            dimensions);
    }

    public int Count => _endpoints.Count;

    public long RelayedAppearanceFrames => Interlocked.Read(ref _relayedAppearanceFrames);

    public long AppearanceBaselineFrames => Interlocked.Read(ref _appearanceBaselineFrames);

    public long RelayedEquipmentFrames => Interlocked.Read(ref _relayedEquipmentFrames);

    public long EquipmentBaselineFrames => Interlocked.Read(ref _equipmentBaselineFrames);

    public long DroppedEquipmentSnapshotUpdates => Interlocked.Read(ref _droppedEquipmentSnapshotUpdates);

    public long PlayerActiveBaselineFrames => Interlocked.Read(ref _playerActiveBaselineFrames);

    public long PlayerDeactivationFrames => Interlocked.Read(ref _playerDeactivationFrames);

    public long RelayedMovementFrames => Interlocked.Read(ref _relayedMovementFrames);

    public long MovementResyncFrames => Interlocked.Read(ref _movementResyncFrames);

    internal RuntimePlayerSpatialIndexSnapshot? PlayerSpatialSnapshot =>
        _interestRouter.PlayerSpatialSnapshot;

    internal RuntimePlayerVisibilitySnapshot? PlayerVisibilitySnapshot =>
        _interestRouter.PlayerVisibilitySnapshot;

    internal RuntimePlayerMovementVisibilityReadinessSnapshot PlayerMovementVisibilityReadinessSnapshot =>
        _movementVisibilityReadiness.Snapshot;

    internal int CollectNearbyPlayers(
        PlayerSlotId subject,
        int radiusSections,
        Span<PlayerSlotId> destination,
        bool includeSubject = false) =>
        _interestRouter.CollectNearbyPlayers(subject, radiusSections, destination, includeSubject);

    internal bool IsPlayerMovementVisibilityReady(PlayerSlotId observer, PlayerSlotId subject) =>
        _movementVisibilityReadiness.IsReady(observer, subject);

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

    internal bool TryGetServerPlayerAppearanceFrame(PlayerHandle player, out OutboundFrame frame) =>
        _serverPlayers.TryGetAppearanceFrame(player, out frame);

    internal bool TryGetServerPlayerHealthFrame(PlayerHandle player, out OutboundFrame frame) =>
        _serverPlayers.TryGetHealthFrame(player, out frame);

    internal bool TryGetServerPlayerMovementFrame(PlayerHandle player, out OutboundFrame frame) =>
        _serverPlayers.TryGetMovementFrame(player, out frame);

    internal bool TryGetServerPlayerItemFrame(
        PlayerHandle player,
        short slot,
        out OutboundFrame frame) =>
        _serverPlayers.TryGetItemFrame(player, slot, out frame);

    internal RuntimePlayerMovementResyncPlan PlanPlayerMovementResyncs(
        PlayerSlotId subject,
        ReadOnlySpan<PlayerSlotId> enteredPeers,
        Span<RuntimePlayerMovementResyncOperation> destination)
    {
        int requiredCapacity = checked(enteredPeers.Length * 2);
        if (destination.Length < requiredCapacity)
        {
            throw new ArgumentException(
                $"Destination must have room for {requiredCapacity} possible resync operations.",
                nameof(destination));
        }

        int planned = 0;
        int missingSnapshots = 0;
        int missingEndpoints = 0;

        for (int i = 0; i < enteredPeers.Length; i++)
        {
            PlayerSlotId peer = enteredPeers[i];
            PlanOnePlayerMovementResync(
                peer,
                subject,
                destination,
                ref planned,
                ref missingSnapshots,
                ref missingEndpoints);
            PlanOnePlayerMovementResync(
                subject,
                peer,
                destination,
                ref planned,
                ref missingSnapshots,
                ref missingEndpoints);
        }

        return new RuntimePlayerMovementResyncPlan(planned, missingSnapshots, missingEndpoints);
    }

    internal bool TryEnqueuePlayerMovementResync(in RuntimePlayerMovementResyncOperation operation)
    {
        if (operation.Recipient == operation.Subject ||
            !_interestRouter.IsPlayerVisible(operation.Recipient.Slot, operation.Subject.Slot) ||
            !TryGetPlayingEndpoint(operation.Recipient, out RuntimeConnectionEndpoint recipient) ||
            !TryGetPlayingEndpoint(operation.Subject, out RuntimeConnectionEndpoint subject) ||
            !subject.TryGetLatestMovementFrame(out OutboundFrame frame))
        {
            return false;
        }

        if (recipient.Outbound.TryEnqueue(frame) != OutboundEnqueueResult.Enqueued)
            return false;

        _movementVisibilityReadiness.MarkReady(operation.Recipient.Slot, operation.Subject.Slot);
        Interlocked.Increment(ref _movementResyncFrames);
        return true;
    }

    public bool TryRegister(GameCommandSourceId source, TerrariaConnectionOutboundQueue outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        if (source.IsSystem)
            return false;

        return _endpoints.TryAdd(source, new RuntimeConnectionEndpoint(outbound));
    }

    public bool TryUnregister(GameCommandSourceId source, out PlayerHandle? playingPlayer)
    {
        playingPlayer = null;
        if (!_endpoints.TryRemove(source, out RuntimeConnectionEndpoint? endpoint))
            return false;

        if (endpoint.TryGetPlayingPlayer(out PlayerHandle player))
        {
            playingPlayer = player;
            Interlocked.CompareExchange(ref _playingEndpoints[player.Slot.Value], null, endpoint);
            _movementVisibilityReadiness.ClearPlayer(player.Slot);
        }

        return true;
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

        var appearance = new TerrariaPlayerAppearanceState(
            normalized.PlayerSlot.Value,
            normalized.SkinVariant,
            normalized.VoiceVariant,
            normalized.VoicePitchOffset,
            normalized.Hair,
            normalized.Name,
            normalized.HairDye,
            normalized.HideVisibleAccessory,
            normalized.HideMisc,
            ToProtocol(normalized.HairColor),
            ToProtocol(normalized.SkinColor),
            ToProtocol(normalized.EyeColor),
            ToProtocol(normalized.ShirtColor),
            ToProtocol(normalized.UnderShirtColor),
            ToProtocol(normalized.PantsColor),
            ToProtocol(normalized.ShoeColor),
            normalized.DifficultyFlags,
            normalized.TorchAndCartFlags,
            normalized.ConsumableUnlockFlags);

        byte[] encoded = TerrariaPlayerAppearanceCodec.Encode(in appearance);
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

        var equipment = new TerrariaPlayerEquipmentState(
            request.PlayerSlot.Value,
            request.SlotId,
            request.Stack,
            request.Prefix,
            request.ItemNetId,
            request.ItemFlags);
        byte[] encoded = TerrariaPlayerEquipmentCodec.Encode(in equipment);
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

        var movement = new TerrariaPlayerMovementState(
            normalized.PlayerSlot.Value,
            normalized.ControlFlags,
            normalized.MovementFlags,
            normalized.MiscFlags1,
            normalized.MiscFlags2,
            normalized.SelectedItem,
            normalized.PositionX,
            normalized.PositionY,
            normalized.HasVelocity,
            normalized.VelocityX,
            normalized.VelocityY,
            normalized.HasMount,
            normalized.MountType,
            normalized.HasPotionOfReturnPositions,
            normalized.PotionOfReturnOriginalPositionX,
            normalized.PotionOfReturnOriginalPositionY,
            normalized.PotionOfReturnHomePositionX,
            normalized.PotionOfReturnHomePositionY,
            normalized.HasCameraTarget,
            normalized.CameraTargetX,
            normalized.CameraTargetY);

        // Cache the current authoritative movement before refreshing visibility. A future AOI-enter
        // resync can therefore use this exact movement as the subject's baseline in the same commit.
        byte[] encoded = TerrariaPlayerMovementEncoder.Encode(in movement);
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

    public void ServerPlayerCreated(in PlayerStateSnapshot player)
    {
        if (!_serverPlayers.TryCreate(in player, out byte[] active, out byte[] movement))
            return;

        Interlocked.Add(ref _playerActiveBaselineFrames, BroadcastToPlaying(active));
        Interlocked.Add(ref _relayedMovementFrames, BroadcastToPlaying(movement));
    }

    public void ServerPlayerAppearanceUpdated(
        PlayerHandle player,
        in ServerPlayerAppearanceState appearance)
    {
        if (_serverPlayers.TryUpdateAppearance(player, in appearance, out byte[] encoded))
            Interlocked.Add(ref _relayedAppearanceFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerVitalsUpdated(
        PlayerHandle player,
        in ServerPlayerVitalsState vitals)
    {
        if (!_serverPlayers.TryUpdateVitals(player, in vitals, out byte[] health, out byte[] mana))
            return;

        Interlocked.Add(ref _serverPlayerHealthFrames, BroadcastToPlaying(health));
        Interlocked.Add(ref _serverPlayerManaFrames, BroadcastToPlaying(mana));
    }

    public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item)
    {
        if (_serverPlayers.TryUpdateItem(player, in item, out byte[] encoded))
            Interlocked.Add(ref _relayedEquipmentFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerMoved(in PlayerStateSnapshot player)
    {
        if (_serverPlayers.TryUpdateMovement(in player, out byte[] encoded))
            Interlocked.Add(ref _relayedMovementFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerDespawned(PlayerHandle player)
    {
        if (_serverPlayers.TryRemove(player, out byte[] inactive))
            Interlocked.Add(ref _playerDeactivationFrames, BroadcastToPlaying(inactive));
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

    private void SynchronizeServerPlayerBaselines(RuntimeConnectionEndpoint recipient)
    {
        ServerPlayerBaselineEnqueueCounts counts = _serverPlayers.EnqueueBaselines(recipient);
        Interlocked.Add(ref _playerActiveBaselineFrames, counts.Active);
        Interlocked.Add(ref _appearanceBaselineFrames, counts.Appearance);
        Interlocked.Add(ref _equipmentBaselineFrames, counts.Equipment);
        Interlocked.Add(ref _serverPlayerHealthFrames, counts.Health);
        Interlocked.Add(ref _serverPlayerManaFrames, counts.Mana);
        Interlocked.Add(ref _movementResyncFrames, counts.Movement);
    }

    private int BroadcastToPlaying(byte[] encoded)
    {
        int enqueued = 0;
        var frame = new OutboundFrame(encoded);
        foreach (RuntimeConnectionEndpoint endpoint in _endpoints.Values)
        {
            if (endpoint.TryGetPlayingSlot(out _) &&
                endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
            {
                enqueued++;
            }
        }

        return enqueued;
    }

    private void ResetMovementVisibilityReadiness(
        PlayerSlotId subject,
        RuntimePlayerVisibilityUpdate visibility,
        ReadOnlySpan<PlayerSlotId> entered,
        ReadOnlySpan<PlayerSlotId> left)
    {
        for (int i = 0; i < visibility.Entered; i++)
            _movementVisibilityReadiness.ClearPair(subject, entered[i]);

        for (int i = 0; i < visibility.Left; i++)
            _movementVisibilityReadiness.ClearPair(subject, left[i]);
    }

    private void PlanOnePlayerMovementResync(
        PlayerSlotId recipientSlot,
        PlayerSlotId subjectSlot,
        Span<RuntimePlayerMovementResyncOperation> destination,
        ref int planned,
        ref int missingSnapshots,
        ref int missingEndpoints)
    {
        if (!TryGetPlayingEndpoint(recipientSlot, out RuntimeConnectionEndpoint recipientEndpoint) ||
            !TryGetPlayingEndpoint(subjectSlot, out RuntimeConnectionEndpoint subjectEndpoint) ||
            !recipientEndpoint.TryGetPlayingPlayer(out PlayerHandle recipient) ||
            !subjectEndpoint.TryGetPlayingPlayer(out PlayerHandle subject))
        {
            missingEndpoints++;
            return;
        }

        if (!subjectEndpoint.TryGetLatestMovementFrame(out _))
        {
            missingSnapshots++;
            return;
        }

        destination[planned++] = new RuntimePlayerMovementResyncOperation(recipient, subject);
    }

    private bool TryGetPlayingEndpoint(PlayerHandle player, out RuntimeConnectionEndpoint endpoint)
    {
        if (!TryGetPlayingEndpoint(player.Slot, out endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle current) ||
            current != player)
        {
            endpoint = null!;
            return false;
        }

        return true;
    }

    private bool TryGetPlayingEndpoint(PlayerSlotId slot, out RuntimeConnectionEndpoint endpoint)
    {
        RuntimeConnectionEndpoint? candidate = Volatile.Read(ref _playingEndpoints[slot.Value]);
        if (candidate is null ||
            !candidate.TryGetPlayingSlot(out PlayerSlotId currentSlot) ||
            currentSlot != slot)
        {
            endpoint = null!;
            return false;
        }

        endpoint = candidate;
        return true;
    }

    private static TerrariaRgbColor ToProtocol(PlayerRgbColor color) => new(color.R, color.G, color.B);
}


internal readonly record struct RuntimePlayerMovementResyncOperation(
    PlayerHandle Recipient,
    PlayerHandle Subject);

internal readonly record struct RuntimePlayerMovementResyncPlan(
    int Planned,
    int MissingSnapshots,
    int MissingEndpoints);
