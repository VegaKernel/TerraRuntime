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
internal sealed class RuntimeConnectionRegistry : IRuntimePlayerEventSink
{
    private const int MaxPlayerSlots = 256;

    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> _endpoints = new();
    private readonly Endpoint?[] _playingEndpoints = new Endpoint?[MaxPlayerSlots];
    private readonly RuntimeInterestRouter _interestRouter;
    private readonly RuntimePlayerMovementVisibilityReadiness _movementVisibilityReadiness = new();
    private long _relayedAppearanceFrames;
    private long _appearanceBaselineFrames;
    private long _relayedMovementFrames;
    private long _movementResyncFrames;

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
            !_interestRouter.IsPlayerVisible(operation.Recipient, operation.Subject) ||
            !TryGetPlayingEndpoint(operation.Recipient, out Endpoint recipient) ||
            !TryGetLatestPlayerMovementFrame(operation.Subject, out OutboundFrame frame))
        {
            return false;
        }

        if (recipient.Outbound.TryEnqueue(frame) != OutboundEnqueueResult.Enqueued)
            return false;

        _movementVisibilityReadiness.MarkReady(operation.Recipient, operation.Subject);
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

    public bool TryUnregister(GameCommandSourceId source, out PlayerSlotId? playingSlot)
    {
        playingSlot = null;
        if (!_endpoints.TryRemove(source, out Endpoint? endpoint))
            return false;

        if (endpoint.TryGetPlayingSlot(out PlayerSlotId slot))
        {
            playingSlot = slot;
            Interlocked.CompareExchange(ref _playingEndpoints[slot.Value], null, endpoint);
            _movementVisibilityReadiness.ClearPlayer(slot);
        }

        return true;
    }

    public void PlayerAppearanceUpdated(GameCommandSourceId source, in PlayerAppearanceCommitRequest request)
    {
        if (!_endpoints.TryGetValue(source, out Endpoint? endpoint))
            return;

        var appearance = new TerrariaPlayerAppearanceState(
            request.PlayerSlot.Value,
            request.SkinVariant,
            request.VoiceVariant,
            request.VoicePitchOffset,
            request.Hair,
            request.Name,
            request.HairDye,
            request.HideVisibleAccessory,
            request.HideMisc,
            ToProtocol(request.HairColor),
            ToProtocol(request.SkinColor),
            ToProtocol(request.EyeColor),
            ToProtocol(request.ShirtColor),
            ToProtocol(request.UnderShirtColor),
            ToProtocol(request.PantsColor),
            ToProtocol(request.ShoeColor),
            request.DifficultyFlags,
            request.TorchAndCartFlags,
            request.ConsumableUnlockFlags);

        byte[] encoded = TerrariaPlayerAppearanceCodec.Encode(in appearance);
        endpoint.UpdateLatestAppearanceFrame(request.PlayerSlot, encoded);

        if (!endpoint.TryGetPlayingSlot(out PlayerSlotId currentSlot) || currentSlot != request.PlayerSlot)
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

    public void PlayerSpawned(GameCommandSourceId source, in PlayerSpawnCommitRequest request)
    {
        if (!_endpoints.TryGetValue(source, out Endpoint? endpoint))
            return;

        float positionX = request.SpawnX * 16f;
        float positionY = request.SpawnY * 16f;
        endpoint.MarkPlaying(request.ClaimedSlot);
        endpoint.UpdatePosition(positionX, positionY);
        Interlocked.Exchange(ref _playingEndpoints[request.ClaimedSlot.Value], endpoint);
        _movementVisibilityReadiness.ClearPlayer(request.ClaimedSlot);

        SynchronizeAppearanceBaselines(source, request.ClaimedSlot, endpoint);

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

    public void PlayerMoved(GameCommandSourceId source, in PlayerMovementCommitRequest request)
    {
        if (!_endpoints.TryGetValue(source, out Endpoint? origin) ||
            !origin.TryGetPlayingSlot(out PlayerSlotId originSlot) ||
            originSlot != request.PlayerSlot)
        {
            return;
        }

        origin.UpdatePosition(request.PositionX, request.PositionY);

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
            request.PlayerSlot,
            request.PositionX,
            request.PositionY,
            entered,
            left);
        ResetMovementVisibilityReadiness(request.PlayerSlot, visibility, entered, left);

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

    public void PlayerDisconnected(GameCommandSourceId source, PlayerSlotId slot)
    {
        _interestRouter.RemovePlayer(slot);
        _movementVisibilityReadiness.ClearPlayer(slot);
        if (_endpoints.TryGetValue(source, out Endpoint? endpoint))
        {
            Interlocked.CompareExchange(ref _playingEndpoints[slot.Value], null, endpoint);
            endpoint.ClearPlaying(slot);
        }
    }

    private void SynchronizeAppearanceBaselines(
        GameCommandSourceId source,
        PlayerSlotId slot,
        Endpoint endpoint)
    {
        bool hasOriginAppearance = endpoint.TryGetLatestAppearanceFrame(slot, out OutboundFrame originAppearance);

        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingSlot(out PlayerSlotId peerSlot))
                continue;

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
        }
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
        PlayerSlotId recipient,
        PlayerSlotId subject,
        Span<RuntimePlayerMovementResyncOperation> destination,
        ref int planned,
        ref int missingSnapshots,
        ref int missingEndpoints)
    {
        if (!TryGetPlayingEndpoint(recipient, out _) ||
            !TryGetPlayingEndpoint(subject, out Endpoint subjectEndpoint))
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

    private sealed class Endpoint
    {
        private int _playingSlot = -1;
        private int _appearanceSlot = -1;
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

        public void MarkPlaying(PlayerSlotId slot) => Volatile.Write(ref _playingSlot, slot.Value);

        public bool TryGetPlayingSlot(out PlayerSlotId slot)
        {
            int value = Volatile.Read(ref _playingSlot);
            if (value < 0)
            {
                slot = default;
                return false;
            }

            slot = new PlayerSlotId(checked((byte)value));
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

        public void ClearPlaying(PlayerSlotId slot)
        {
            if (Interlocked.CompareExchange(ref _playingSlot, -1, slot.Value) != slot.Value)
                return;

            _hasPosition = false;
            if (Interlocked.CompareExchange(ref _appearanceSlot, -1, slot.Value) == slot.Value)
                Volatile.Write(ref _latestAppearanceFrame, null);
            Volatile.Write(ref _latestMovementFrame, null);
        }
    }
}

internal readonly record struct RuntimePlayerMovementResyncOperation(
    PlayerSlotId Recipient,
    PlayerSlotId Subject);

internal readonly record struct RuntimePlayerMovementResyncPlan(
    int Planned,
    int MissingSnapshots,
    int MissingEndpoints);
