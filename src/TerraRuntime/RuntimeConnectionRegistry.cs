using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Tracks live connection outbound queues independently from socket ownership and fans out
/// authoritative player events only to clients that have completed the spawn transition.
/// </summary>
internal sealed class RuntimeConnectionRegistry : IRuntimePlayerEventSink
{
    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> _endpoints = new();
    private long _relayedMovementFrames;

    public int Count => _endpoints.Count;

    public long RelayedMovementFrames => Interlocked.Read(ref _relayedMovementFrames);

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
            playingSlot = slot;

        return true;
    }

    public void PlayerSpawned(GameCommandSourceId source, PlayerSlotId slot)
    {
        if (_endpoints.TryGetValue(source, out Endpoint? endpoint))
            endpoint.MarkPlaying(slot);
    }

    public void PlayerMoved(GameCommandSourceId source, in PlayerMovementCommitRequest request)
    {
        if (!_endpoints.TryGetValue(source, out Endpoint? origin) ||
            !origin.TryGetPlayingSlot(out PlayerSlotId originSlot) ||
            originSlot != request.PlayerSlot)
        {
            return;
        }

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
        var frame = new OutboundFrame(encoded);

        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
        {
            if (pair.Key == source || !pair.Value.TryGetPlayingSlot(out _))
                continue;

            if (pair.Value.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _relayedMovementFrames);
        }
    }

    public void PlayerDisconnected(GameCommandSourceId source, PlayerSlotId slot)
    {
        if (_endpoints.TryGetValue(source, out Endpoint? endpoint))
            endpoint.ClearPlaying(slot);
    }

    private sealed class Endpoint
    {
        private int _playingSlot = -1;

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

        public void ClearPlaying(PlayerSlotId slot) =>
            Interlocked.CompareExchange(ref _playingSlot, -1, slot.Value);
    }
}
