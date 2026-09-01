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
    private const int MaxEquipmentSnapshotsPerPlayer = VanillaPlayerItemSlotCatalog.RelayableCount;

    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> _endpoints = new();
    private readonly Endpoint?[] _playingEndpoints = new Endpoint?[MaxPlayerSlots];
    private readonly ServerPlayerReplica?[] _serverPlayers = new ServerPlayerReplica?[MaxPlayerSlots];
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
        if (!TryGetPlayingEndpoint(slot, out Endpoint endpoint))
        {
            frame = default;
            return false;
        }

        return endpoint.TryGetLatestAppearanceFrame(slot, out frame);
    }

    internal bool TryGetLatestPlayerMovementFrame(PlayerSlotId slot, out OutboundFrame frame)
    {
        if (!TryGetPlayingEndpoint(slot, out Endpoint endpoint))
        {
            frame = default;
            return false;
        }

        return endpoint.TryGetLatestMovementFrame(out frame);
    }

    internal bool TryGetServerPlayerAppearanceFrame(PlayerHandle player, out OutboundFrame frame) =>
        TryGetServerPlayerFrame(player, static replica => replica.Appearance, out frame);

    internal bool TryGetServerPlayerHealthFrame(PlayerHandle player, out OutboundFrame frame) =>
        TryGetServerPlayerFrame(player, static replica => replica.Health, out frame);

    internal bool TryGetServerPlayerMovementFrame(PlayerHandle player, out OutboundFrame frame) =>
        TryGetServerPlayerFrame(player, static replica => replica.Movement, out frame);

    internal bool TryGetServerPlayerItemFrame(
        PlayerHandle player,
        short slot,
        out OutboundFrame frame)
    {
        if (!TryGetServerPlayer(player, out ServerPlayerReplica replica) ||
            !replica.Items.TryGetValue(slot, out byte[]? encoded))
        {
            frame = default;
            return false;
        }

        frame = new OutboundFrame(encoded);
        return true;
    }

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
            !TryGetPlayingEndpoint(operation.Recipient, out Endpoint recipient) ||
            !TryGetPlayingEndpoint(operation.Subject, out Endpoint subject) ||
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

        return _endpoints.TryAdd(source, new Endpoint(outbound));
    }

    public bool TryUnregister(GameCommandSourceId source, out PlayerHandle? playingPlayer)
    {
        playingPlayer = null;
        if (!_endpoints.TryRemove(source, out Endpoint? endpoint))
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

        if (!_endpoints.TryGetValue(source, out Endpoint? endpoint))
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
        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingSlot(out _))
                continue;

            if (pair.Value.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _relayedAppearanceFrames);
        }
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        PlayerEquipmentCommitRequest normalized = VanillaPlayerItemNormalizer.Normalize(in request);
        GameCommandSourceId source = connection.Source;
        if (connection.Player.Slot != normalized.PlayerSlot ||
            !VanillaPlayerItemSlotCatalog.IsValid(normalized.SlotId) ||
            !VanillaPlayerItemSlotCatalog.CanRelay(normalized.SlotId))
            return;

        if (!_endpoints.TryGetValue(source, out Endpoint? endpoint))
            return;

        var equipment = new TerrariaPlayerEquipmentState(
            normalized.PlayerSlot.Value,
            normalized.SlotId,
            normalized.Stack,
            normalized.Prefix,
            normalized.ItemNetId,
            normalized.ItemFlags);
        byte[] encoded = TerrariaPlayerEquipmentCodec.Encode(in equipment);
        if (!endpoint.UpdateLatestEquipmentFrame(normalized.PlayerSlot, normalized.SlotId, encoded))
            Interlocked.Increment(ref _droppedEquipmentSnapshotUpdates);

        if (!endpoint.TryGetPlayingPlayer(out PlayerHandle currentPlayer) || currentPlayer != connection.Player)
            return;

        var frame = new OutboundFrame(encoded);
        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
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

        if (!_endpoints.TryGetValue(source, out Endpoint? endpoint))
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
        if (!_endpoints.TryGetValue(source, out Endpoint? origin) ||
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

        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
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
        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingSlot(out _))
                continue;

            if (pair.Value.Outbound.TryEnqueue(inactiveFrame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _playerDeactivationFrames);
        }

        if (_endpoints.TryGetValue(source, out Endpoint? endpoint))
        {
            Interlocked.CompareExchange(ref _playingEndpoints[slot.Value], null, endpoint);
            endpoint.ClearPlaying(connection.Player);
        }
    }

    public void ServerPlayerCreated(in PlayerStateSnapshot player)
    {
        if (!player.Player.IsAssigned)
            return;

        byte[] active = TerrariaPlayerActiveEncoder.Encode(player.Player.Slot.Value, active: true);
        byte[] movement = EncodeServerPlayerMovement(in player);
        var replica = new ServerPlayerReplica(player.Player, active)
        {
            Movement = movement
        };
        _serverPlayers[player.Player.Slot.Value] = replica;
        Interlocked.Add(ref _playerActiveBaselineFrames, BroadcastToPlaying(active));
        Interlocked.Add(ref _relayedMovementFrames, BroadcastToPlaying(movement));
    }

    public void ServerPlayerAppearanceUpdated(
        PlayerHandle player,
        in ServerPlayerAppearanceState appearance)
    {
        if (!TryGetServerPlayer(player, out ServerPlayerReplica replica))
            return;

        byte[] encoded = EncodeServerPlayerAppearance(player.Slot, in appearance);
        replica.Appearance = encoded;
        Interlocked.Add(ref _relayedAppearanceFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerVitalsUpdated(
        PlayerHandle player,
        in ServerPlayerVitalsState vitals)
    {
        if (!TryGetServerPlayer(player, out ServerPlayerReplica replica))
            return;

        var health = new TerrariaPlayerHealthState(player.Slot.Value, vitals.Life, vitals.MaxLife);
        var mana = new TerrariaPlayerManaState(player.Slot.Value, vitals.Mana, vitals.MaxMana);
        replica.Health = TerrariaPlayerVitalsCodec.EncodeHealth(in health);
        replica.Mana = TerrariaPlayerVitalsCodec.EncodeMana(in mana);
        Interlocked.Add(ref _serverPlayerHealthFrames, BroadcastToPlaying(replica.Health));
        Interlocked.Add(ref _serverPlayerManaFrames, BroadcastToPlaying(replica.Mana));
    }

    public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item)
    {
        if (!VanillaPlayerItemSlotCatalog.CanRelay(item.Slot) ||
            !TryGetServerPlayer(player, out ServerPlayerReplica replica))
        {
            return;
        }

        byte[] encoded = EncodeServerPlayerItem(player.Slot, in item);
        if (item.IsEmpty)
            replica.Items.Remove(item.Slot);
        else
            replica.Items[item.Slot] = encoded;
        Interlocked.Add(ref _relayedEquipmentFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerMoved(in PlayerStateSnapshot player)
    {
        if (!TryGetServerPlayer(player.Player, out ServerPlayerReplica replica))
            return;

        byte[] encoded = EncodeServerPlayerMovement(in player);
        replica.Movement = encoded;
        Interlocked.Add(ref _relayedMovementFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerDespawned(PlayerHandle player)
    {
        if (!TryGetServerPlayer(player, out _))
            return;

        _serverPlayers[player.Slot.Value] = null;
        byte[] inactive = TerrariaPlayerActiveEncoder.Encode(player.Slot.Value, active: false);
        Interlocked.Add(ref _playerDeactivationFrames, BroadcastToPlaying(inactive));
    }

    private void SynchronizePlayerBaselines(
        GameCommandSourceId source,
        PlayerSlotId slot,
        Endpoint endpoint)
    {
        var originActive = new OutboundFrame(
            TerrariaPlayerActiveEncoder.Encode(slot.Value, active: true));
        bool hasOriginAppearance = endpoint.TryGetLatestAppearanceFrame(slot, out OutboundFrame originAppearance);

        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
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

    private void SynchronizeServerPlayerBaselines(Endpoint recipient)
    {
        for (int slot = 0; slot < _serverPlayers.Length; slot++)
        {
            ServerPlayerReplica? replica = _serverPlayers[slot];
            if (replica is null)
                continue;

            EnqueueBaseline(recipient, replica.Active, ref _playerActiveBaselineFrames);
            EnqueueBaseline(recipient, replica.Appearance, ref _appearanceBaselineFrames);
            foreach (byte[] item in replica.Items.Values)
                EnqueueBaseline(recipient, item, ref _equipmentBaselineFrames);
            EnqueueBaseline(recipient, replica.Health, ref _serverPlayerHealthFrames);
            EnqueueBaseline(recipient, replica.Mana, ref _serverPlayerManaFrames);
            EnqueueBaseline(recipient, replica.Movement, ref _movementResyncFrames);
        }
    }

    private static void EnqueueBaseline(Endpoint recipient, byte[]? encoded, ref long counter)
    {
        if (encoded is not null &&
            recipient.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
        {
            Interlocked.Increment(ref counter);
        }
    }

    private int BroadcastToPlaying(byte[] encoded)
    {
        int enqueued = 0;
        var frame = new OutboundFrame(encoded);
        foreach (Endpoint endpoint in _endpoints.Values)
        {
            if (endpoint.TryGetPlayingSlot(out _) &&
                endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
            {
                enqueued++;
            }
        }

        return enqueued;
    }

    private bool TryGetServerPlayer(PlayerHandle player, out ServerPlayerReplica replica)
    {
        ServerPlayerReplica? current = player.IsAssigned
            ? _serverPlayers[player.Slot.Value]
            : null;
        if (current is null || current.Player != player)
        {
            replica = null!;
            return false;
        }

        replica = current;
        return true;
    }

    private bool TryGetServerPlayerFrame(
        PlayerHandle player,
        Func<ServerPlayerReplica, byte[]?> selector,
        out OutboundFrame frame)
    {
        if (!TryGetServerPlayer(player, out ServerPlayerReplica replica) ||
            selector(replica) is not byte[] encoded)
        {
            frame = default;
            return false;
        }

        frame = new OutboundFrame(encoded);
        return true;
    }

    private static byte[] EncodeServerPlayerAppearance(
        PlayerSlotId player,
        in ServerPlayerAppearanceState appearance)
    {
        var state = new TerrariaPlayerAppearanceState(
            player.Value,
            appearance.SkinVariant,
            appearance.VoiceVariant,
            appearance.VoicePitchOffset,
            appearance.Hair,
            appearance.Name,
            appearance.HairDye,
            appearance.HideVisibleAccessory,
            appearance.HideMisc,
            ToProtocol(appearance.HairColor),
            ToProtocol(appearance.SkinColor),
            ToProtocol(appearance.EyeColor),
            ToProtocol(appearance.ShirtColor),
            ToProtocol(appearance.UnderShirtColor),
            ToProtocol(appearance.PantsColor),
            ToProtocol(appearance.ShoeColor),
            appearance.DifficultyFlags,
            appearance.TorchAndCartFlags,
            appearance.ConsumableUnlockFlags);
        return TerrariaPlayerAppearanceCodec.Encode(in state);
    }

    private static byte[] EncodeServerPlayerItem(
        PlayerSlotId player,
        in ServerPlayerItemState item)
    {
        var state = new TerrariaPlayerEquipmentState(
            player.Value,
            item.Slot,
            item.Stack,
            checked((byte)item.Prefix.Value),
            checked((short)item.ItemType.Value),
            item.ItemFlags);
        return TerrariaPlayerEquipmentCodec.Encode(in state);
    }

    private static byte[] EncodeServerPlayerMovement(in PlayerStateSnapshot player)
    {
        var state = new TerrariaPlayerMovementState(
            player.Player.Slot.Value,
            player.ControlFlags,
            player.MovementFlags,
            player.MiscFlags1,
            player.MiscFlags2,
            player.SelectedItem,
            player.PositionX,
            player.PositionY,
            HasVelocity: true,
            player.VelocityX,
            player.VelocityY,
            HasMount: player.MountType != 0,
            player.MountType,
            HasPotionOfReturnPositions: false,
            player.PotionOfReturnOriginalPositionX,
            player.PotionOfReturnOriginalPositionY,
            player.PotionOfReturnHomePositionX,
            player.PotionOfReturnHomePositionY,
            HasCameraTarget: false,
            player.CameraTargetX,
            player.CameraTargetY);
        return TerrariaPlayerMovementEncoder.Encode(in state);
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
        if (!TryGetPlayingEndpoint(recipientSlot, out Endpoint recipientEndpoint) ||
            !TryGetPlayingEndpoint(subjectSlot, out Endpoint subjectEndpoint) ||
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

    private bool TryGetPlayingEndpoint(PlayerHandle player, out Endpoint endpoint)
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

    private bool TryGetPlayingEndpoint(PlayerSlotId slot, out Endpoint endpoint)
    {
        Endpoint? candidate = Volatile.Read(ref _playingEndpoints[slot.Value]);
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

    private static TerrariaRgbColor ToProtocol(PlayerRgbColor color) =>
        new(color.R, color.G, color.B);

    private sealed class ServerPlayerReplica(PlayerHandle player, byte[] active)
    {
        public PlayerHandle Player { get; } = player;

        public byte[] Active { get; } = active;

        public byte[]? Appearance { get; set; }

        public SortedDictionary<short, byte[]> Items { get; } = [];

        public byte[]? Health { get; set; }

        public byte[]? Mana { get; set; }

        public byte[]? Movement { get; set; }
    }

    private sealed class Endpoint
    {
        private readonly object _equipmentGate = new();
        private readonly SortedDictionary<short, byte[]> _equipmentFrames = [];
        private int _playingSlot = -1;
        private ulong _playingGeneration;
        private int _appearanceSlot = -1;
        private int _equipmentOwnerSlot = -1;
        private bool _hasPosition;
        private float _positionX;
        private float _positionY;
        private byte[]? _latestAppearanceFrame;
        private byte[]? _latestMovementFrame;

        public Endpoint(TerrariaConnectionOutboundQueue outbound)
        {
            Outbound = outbound;
        }

        public TerrariaConnectionOutboundQueue Outbound { get; }

        public void MarkPlaying(PlayerHandle player)
        {
            Volatile.Write(ref _playingGeneration, player.Generation.Value);
            Volatile.Write(ref _playingSlot, player.Slot.Value);
        }

        public bool TryGetPlayingPlayer(out PlayerHandle player)
        {
            int slotValue = Volatile.Read(ref _playingSlot);
            ulong generation = Volatile.Read(ref _playingGeneration);
            if (slotValue < 0 ||
                generation == 0 ||
                slotValue != Volatile.Read(ref _playingSlot))
            {
                player = default;
                return false;
            }

            player = new PlayerHandle(
                new PlayerSlotId(checked((byte)slotValue)),
                new PlayerSessionGeneration(generation));
            return true;
        }

        public bool TryGetPlayingSlot(out PlayerSlotId slot)
        {
            if (!TryGetPlayingPlayer(out PlayerHandle player))
            {
                slot = default;
                return false;
            }

            slot = player.Slot;
            return true;
        }

        public void UpdatePosition(float positionX, float positionY)
        {
            _positionX = positionX;
            _positionY = positionY;
            _hasPosition = true;
        }

        public void UpdateLatestAppearanceFrame(PlayerSlotId slot, byte[] encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);
            Volatile.Write(ref _latestAppearanceFrame, encoded);
            Volatile.Write(ref _appearanceSlot, slot.Value);
        }

        public bool TryGetLatestAppearanceFrame(PlayerSlotId expectedSlot, out OutboundFrame frame)
        {
            int appearanceSlot = Volatile.Read(ref _appearanceSlot);
            byte[]? encoded = Volatile.Read(ref _latestAppearanceFrame);
            if (appearanceSlot != expectedSlot.Value || encoded is null)
            {
                frame = default;
                return false;
            }

            frame = new OutboundFrame(encoded);
            return true;
        }

        public bool UpdateLatestEquipmentFrame(PlayerSlotId ownerSlot, short equipmentSlot, byte[] encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);
            lock (_equipmentGate)
            {
                if (_equipmentOwnerSlot != ownerSlot.Value)
                {
                    _equipmentFrames.Clear();
                    _equipmentOwnerSlot = ownerSlot.Value;
                }

                if (_equipmentFrames.ContainsKey(equipmentSlot))
                {
                    _equipmentFrames[equipmentSlot] = encoded;
                    return true;
                }

                if (_equipmentFrames.Count >= MaxEquipmentSnapshotsPerPlayer)
                    return false;

                _equipmentFrames.Add(equipmentSlot, encoded);
                return true;
            }
        }

        public int EnqueueEquipmentBaselineTo(Endpoint recipient, PlayerSlotId expectedOwnerSlot)
        {
            int enqueued = 0;
            lock (_equipmentGate)
            {
                if (_equipmentOwnerSlot != expectedOwnerSlot.Value)
                    return 0;

                foreach (byte[] encoded in _equipmentFrames.Values)
                {
                    if (recipient.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
                        enqueued++;
                }
            }

            return enqueued;
        }

        public void UpdateLatestMovementFrame(byte[] encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);
            Volatile.Write(ref _latestMovementFrame, encoded);
        }

        public bool TryGetLatestMovementFrame(out OutboundFrame frame)
        {
            byte[]? encoded = Volatile.Read(ref _latestMovementFrame);
            if (encoded is null)
            {
                frame = default;
                return false;
            }

            frame = new OutboundFrame(encoded);
            return true;
        }

        public RuntimePlayerInterestState CreateInterestState(PlayerSlotId slot) =>
            new(slot, _hasPosition, _positionX, _positionY);

        public void ClearPlaying(PlayerHandle player)
        {
            if (Volatile.Read(ref _playingGeneration) != player.Generation.Value ||
                Interlocked.CompareExchange(ref _playingSlot, -1, player.Slot.Value) != player.Slot.Value)
                return;

            Volatile.Write(ref _playingGeneration, 0);
            _hasPosition = false;
            if (Interlocked.CompareExchange(ref _appearanceSlot, -1, player.Slot.Value) == player.Slot.Value)
                Volatile.Write(ref _latestAppearanceFrame, null);

            lock (_equipmentGate)
            {
                if (_equipmentOwnerSlot == player.Slot.Value)
                {
                    _equipmentOwnerSlot = -1;
                    _equipmentFrames.Clear();
                }
            }

            Volatile.Write(ref _latestMovementFrame, null);
        }
    }
}

internal readonly record struct RuntimePlayerMovementResyncOperation(
    PlayerHandle Recipient,
    PlayerHandle Subject);

internal readonly record struct RuntimePlayerMovementResyncPlan(
    int Planned,
    int MissingSnapshots,
    int MissingEndpoints);
