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
    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> _endpoints = new();
    private readonly Endpoint?[] _playingEndpoints = new Endpoint?[256];
    private readonly RuntimeInterestRouter _interestRouter;
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

    public long RelayedMovementFrames => Interlocked.Read(ref _relayedMovementFrames);

    public long MovementResyncFrames => Interlocked.Read(ref _movementResyncFrames);

    internal RuntimePlayerSpatialIndexSnapshot? PlayerSpatialSnapshot =>
        _interestRouter.PlayerSpatialSnapshot;

    internal RuntimePlayerVisibilitySnapshot? PlayerVisibilitySnapshot =>
        _interestRouter.PlayerVisibilitySnapshot;

    internal int CollectNearbyPlayers(
        PlayerSlotId subject,
        int radiusSections,
        Span<PlayerSlotId> destination,
        bool includeSubject = false) =>
        _interestRouter.CollectNearbyPlayers(subject, radiusSections, destination, includeSubject);

    internal bool TryGetLatestPlayerMovementFrame(PlayerSlotId slot, out OutboundFrame frame)
    {
        if (!TryGetPlayingEndpoint(slot, out Endpoint? endpoint))
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
            PlanOne(peer, subject);
            PlanOne(subject, peer);
        }

        return new RuntimePlayerMovementResyncPlan(planned, missingSnapshots, missingEndpoints);

        void PlanOne(PlayerSlotId recipient, PlayerSlotId source)
        {
            if (!TryGetPlayingEndpoint(recipient, out _) ||
                !TryGetPlayingEndpoint(source, out Endpoint? sourceEndpoint))
            {
                missingEndpoints++;
                return;
            }

            if (!sourceEndpoint.TryGetLatestMovementFrame(out _))
            {
                missingSnapshots++;
                return;
            }

            destination[planned++] = new RuntimePlayerMovementResyncOperation(recipient, source);
        }
    }

    internal bool TryEnqueuePlayerMovementResync(in RuntimePlayerMovementResyncOperation operation)
    {
        if (operation.Recipient == operation.Subject ||
            !TryGetPlayingEndpoint(operation.Recipient, out Endpoint? recipient) ||
            !TryGetLatestPlayerMovementFrame(operation.Subject, out OutboundFrame frame))
        {
            return false;
        }

        if (recipient.Outbound.TryEnqueue(frame) != OutboundEnqueueResult.Enqueued)
            return false;

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
        }

        return true;
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
        _interestRouter.TrackPlayer(request.ClaimedSlot, positionX, positionY);
    }

    public void PlayerMoved(GameCommandSourceId source, in PlayerMovementCommitRequest request)
    {
        if (!_endpoints.TryGetValue(source, out Endpoint? origin) ||
            !origin.TryGetPlayingSlot(out PlayerSlotId originSlot) ||
            originSlot != request.PlayerSlot)
        {
            return;
        }

        // Track authoritative positions even while interest management is disabled. A live enable
        // can therefore start from current state instead of waiting for every player to move again.
        origin.UpdatePosition(request.PositionX, request.PositionY);
        _interestRouter.TrackPlayer(request.PlayerSlot, request.PositionX, request.PositionY);
        RuntimePlayerInterestState subject = origin.CreateInterestState(originSlot);

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

        byte[] encoded = TerrariaPlayerMovementEncoder.Encode(in movement);
        origin.UpdateLatestMovementFrame(encoded);
        var frame = new OutboundFrame(encoded);

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
        if (_endpoints.TryGetValue(source, out Endpoint? endpoint))
        {
            Interlocked.CompareExchange(ref _playingEndpoints[slot.Value], null, endpoint);
            endpoint.ClearPlaying(slot);
        }
    }

    private bool TryGetPlayingEndpoint(PlayerSlotId slot, out Endpoint? endpoint)
    {
        endpoint = Volatile.Read(ref _playingEndpoints[slot.Value]);
        if (endpoint is null ||
            !endpoint.TryGetPlayingSlot(out PlayerSlotId currentSlot) ||
            currentSlot != slot)
        {
            endpoint = null;
            return false;
        }

        return true;
    }

    private sealed class Endpoint
    {
        private int _playingSlot = -1;
        private bool _hasPosition;
        private float _positionX;
        private float _positionY;
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
