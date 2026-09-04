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

    public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest submitted) =>
        PlayerHealthUpdatedCore(connection, in submitted, includeOrigin: false);

    public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest submitted) =>
        PlayerHealthUpdatedCore(connection, in submitted, includeOrigin: true);

    private void PlayerHealthUpdatedCore(
        ConnectionHandle connection,
        in PlayerHealthCommitRequest submitted,
        bool includeOrigin)
    {
        if (!connection.IsAssigned || connection.Player.Slot != submitted.PlayerSlot)
            return;

        PlayerHealthCommitRequest request = VanillaPlayerVitalsRules.NormalizeHealth(in submitted);
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
        if (includeOrigin && origin.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
            Interlocked.Increment(ref _relayedHealthFrames);

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
        private OwnedFrame? healthFrame;
        private OwnedFrame? manaFrame;
        private int playingSlot = -1;
        private ulong playingGeneration;

        public Endpoint(TerrariaConnectionOutboundQueue outbound)
        {
            Outbound = outbound;
        }

        public TerrariaConnectionOutboundQueue Outbound { get; }

        public void UpdateHealth(PlayerHandle player, byte[] encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);
            Volatile.Write(ref healthFrame, new OwnedFrame(player, encoded));
        }

        public void UpdateMana(PlayerHandle player, byte[] encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);
            Volatile.Write(ref manaFrame, new OwnedFrame(player, encoded));
        }

        public void MarkPlaying(PlayerHandle player)
        {
            Volatile.Write(ref playingGeneration, player.Generation.Value);
            Volatile.Write(ref playingSlot, player.Slot.Value);
        }

        public bool IsPlaying(PlayerHandle expected) =>
            Volatile.Read(ref playingSlot) == expected.Slot.Value &&
            Volatile.Read(ref playingGeneration) == expected.Generation.Value;

        public bool TryGetPlayingPlayer(out PlayerHandle player)
        {
            int slot = Volatile.Read(ref playingSlot);
            ulong generation = Volatile.Read(ref playingGeneration);
            if (slot < 0 || generation == 0)
            {
                player = default;
                return false;
            }

            player = new PlayerHandle(
                new PlayerSlotId(checked((byte)slot)),
                new PlayerSessionGeneration(generation));
            return true;
        }

        public bool TryGetHealth(PlayerHandle expected, out OutboundFrame frame) =>
            TryGetFrame(Volatile.Read(ref healthFrame), expected, out frame);

        public bool TryGetMana(PlayerHandle expected, out OutboundFrame frame) =>
            TryGetFrame(Volatile.Read(ref manaFrame), expected, out frame);

        public void Clear(PlayerHandle expected)
        {
            ClearFrame(ref healthFrame, expected);
            ClearFrame(ref manaFrame, expected);

            if (Volatile.Read(ref playingGeneration) != expected.Generation.Value ||
                Interlocked.CompareExchange(ref playingSlot, -1, expected.Slot.Value) != expected.Slot.Value)
            {
                return;
            }

            Volatile.Write(ref playingGeneration, 0);
        }

        private static bool TryGetFrame(
            OwnedFrame? owned,
            PlayerHandle expected,
            out OutboundFrame frame)
        {
            if (owned is null || owned.Owner != expected)
            {
                frame = default;
                return false;
            }

            frame = new OutboundFrame(owned.Encoded);
            return true;
        }

        private static void ClearFrame(ref OwnedFrame? target, PlayerHandle expected)
        {
            while (true)
            {
                OwnedFrame? current = Volatile.Read(ref target);
                if (current is null || current.Owner != expected)
                    return;

                if (ReferenceEquals(Interlocked.CompareExchange(ref target, null, current), current))
                    return;
            }
        }
    }

    private sealed record OwnedFrame(PlayerHandle Owner, byte[] Encoded);
}
