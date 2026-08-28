using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Owns bounded network replication state for player health and mana independently from the
/// connection registry. Authoritative state remains in <see cref="ServerRuntimeState"/>.
/// </summary>
internal sealed class RuntimePlayerVitalsReplicator
{
    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> _endpoints = new();
    private long _relayedHealthFrames;
    private long _healthBaselineFrames;
    private long _manaBaselineFrames;

    public int Count => _endpoints.Count;

    public long RelayedHealthFrames => Interlocked.Read(ref _relayedHealthFrames);

    public long HealthBaselineFrames => Interlocked.Read(ref _healthBaselineFrames);

    public long ManaBaselineFrames => Interlocked.Read(ref _manaBaselineFrames);

    public bool TryRegister(GameCommandSourceId source, TerrariaConnectionOutboundQueue outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        if (source.IsSystem)
            return false;

        return _endpoints.TryAdd(source, new Endpoint(outbound));
    }

    public bool TryUnregister(GameCommandSourceId source) =>
        _endpoints.TryRemove(source, out _);

    public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest submitted)
    {
        if (!connection.IsAssigned || connection.Player.Slot != submitted.PlayerSlot)
            return;

        PlayerHealthCommitRequest request = VanillaPlayerHealthNormalizer.Normalize(in submitted);
        if (!_endpoints.TryGetValue(connection.Source, out Endpoint? origin))
            return;

        var state = new TerrariaPlayerHealthState(
            connection.Player.Slot.Value,
            request.Life,
            request.MaxLife);
        byte[] encoded = TerrariaPlayerVitalsCodec.EncodeHealth(in state);
        origin.UpdateHealth(connection.Player, encoded);

        if (!origin.IsPlaying(connection.Player))
            return;

        var frame = new OutboundFrame(encoded);
        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
        {
            if (pair.Key == connection.Source || !pair.Value.TryGetPlayingPlayer(out _))
                continue;

            if (pair.Value.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref _relayedHealthFrames);
        }
    }

    public void PlayerManaUpdated(ConnectionHandle connection, in PlayerManaCommitRequest request)
    {
        if (!connection.IsAssigned || connection.Player.Slot != request.PlayerSlot)
            return;

        if (!_endpoints.TryGetValue(connection.Source, out Endpoint? origin))
            return;

        var state = new TerrariaPlayerManaState(
            connection.Player.Slot.Value,
            request.Mana,
            request.MaxMana);
        byte[] encoded = TerrariaPlayerVitalsCodec.EncodeMana(in state);
        origin.UpdateMana(connection.Player, encoded);

        // Vanilla 1.4.5.8 stores packet 42 but does not immediately relay it from the handler.
        // Mana reaches peers through the player synchronization baseline instead.
    }

    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        if (!connection.IsAssigned || connection.Player.Slot != request.ClaimedSlot)
            return;

        if (!_endpoints.TryGetValue(connection.Source, out Endpoint? origin))
            return;

        origin.MarkPlaying(connection.Player);

        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in _endpoints)
        {
            if (pair.Key == connection.Source ||
                !pair.Value.TryGetPlayingPlayer(out PlayerHandle peerPlayer))
            {
                continue;
            }

            Endpoint peer = pair.Value;
            if (origin.TryGetHealth(connection.Player, out OutboundFrame originHealth) &&
                peer.Outbound.TryEnqueue(originHealth) == OutboundEnqueueResult.Enqueued)
            {
                Interlocked.Increment(ref _healthBaselineFrames);
            }

            if (origin.TryGetMana(connection.Player, out OutboundFrame originMana) &&
                peer.Outbound.TryEnqueue(originMana) == OutboundEnqueueResult.Enqueued)
            {
                Interlocked.Increment(ref _manaBaselineFrames);
            }

            if (peer.TryGetHealth(peerPlayer, out OutboundFrame peerHealth) &&
                origin.Outbound.TryEnqueue(peerHealth) == OutboundEnqueueResult.Enqueued)
            {
                Interlocked.Increment(ref _healthBaselineFrames);
            }

            if (peer.TryGetMana(peerPlayer, out OutboundFrame peerMana) &&
                origin.Outbound.TryEnqueue(peerMana) == OutboundEnqueueResult.Enqueued)
            {
                Interlocked.Increment(ref _manaBaselineFrames);
            }
        }
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        if (_endpoints.TryGetValue(connection.Source, out Endpoint? endpoint))
            endpoint.Clear(connection.Player);
    }

    private sealed class Endpoint
    {
        private readonly object _gate = new();
        private PlayerHandle _snapshotOwner;
        private PlayerHandle _playingPlayer;
        private byte[]? _healthFrame;
        private byte[]? _manaFrame;

        public Endpoint(TerrariaConnectionOutboundQueue outbound)
        {
            Outbound = outbound;
        }

        public TerrariaConnectionOutboundQueue Outbound { get; }

        public void UpdateHealth(PlayerHandle player, byte[] encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);
            lock (_gate)
            {
                ResetSnapshotsIfOwnerChanged(player);
                _healthFrame = encoded;
            }
        }

        public void UpdateMana(PlayerHandle player, byte[] encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);
            lock (_gate)
            {
                ResetSnapshotsIfOwnerChanged(player);
                _manaFrame = encoded;
            }
        }

        public void MarkPlaying(PlayerHandle player)
        {
            lock (_gate)
            {
                ResetSnapshotsIfOwnerChanged(player);
                _playingPlayer = player;
            }
        }

        public bool IsPlaying(PlayerHandle expected)
        {
            lock (_gate)
                return _playingPlayer == expected;
        }

        public bool TryGetPlayingPlayer(out PlayerHandle player)
        {
            lock (_gate)
            {
                player = _playingPlayer;
                return player.IsAssigned;
            }
        }

        public bool TryGetHealth(PlayerHandle expected, out OutboundFrame frame)
        {
            lock (_gate)
            {
                if (_snapshotOwner != expected || _healthFrame is null)
                {
                    frame = default;
                    return false;
                }

                frame = new OutboundFrame(_healthFrame);
                return true;
            }
        }

        public bool TryGetMana(PlayerHandle expected, out OutboundFrame frame)
        {
            lock (_gate)
            {
                if (_snapshotOwner != expected || _manaFrame is null)
                {
                    frame = default;
                    return false;
                }

                frame = new OutboundFrame(_manaFrame);
                return true;
            }
        }

        public void Clear(PlayerHandle expected)
        {
            lock (_gate)
            {
                if (_snapshotOwner == expected)
                {
                    _snapshotOwner = default;
                    _healthFrame = null;
                    _manaFrame = null;
                }

                if (_playingPlayer == expected)
                    _playingPlayer = default;
            }
        }

        private void ResetSnapshotsIfOwnerChanged(PlayerHandle player)
        {
            if (_snapshotOwner == player)
                return;

            _snapshotOwner = player;
            _healthFrame = null;
            _manaFrame = null;
            if (_playingPlayer != player)
                _playingPlayer = default;
        }
    }
}
