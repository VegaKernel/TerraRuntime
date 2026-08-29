using System.Collections.Concurrent;
using System.Text;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Transport-only projection of authoritative sign reads and commits. Endpoints are transport state; sign authority
/// stays in the game-thread store/processor. Changed text is broadcast to every playing client except the sender,
/// matching the pinned TerrariaServer 1.4.5.8 sign update path.
/// </summary>
internal sealed class RuntimeSignReplicationRegistry : IRuntimePlayerEventSink
{
    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();

    public long ReadFrames { get; private set; }
    public long UpdateFrames { get; private set; }
    public long RejectedFrames { get; private set; }

    public bool TryRegister(GameCommandSourceId source, TerrariaConnectionOutboundQueue outbound)
    {
        if (source.IsSystem)
            return false;
        ArgumentNullException.ThrowIfNull(outbound);
        return endpoints.TryAdd(source, new Endpoint(outbound));
    }

    public bool TryUnregister(GameCommandSourceId source) =>
        !source.IsSystem && endpoints.TryRemove(source, out _);

    public bool TrySendRead(ConnectionHandle connection, WorldSign sign)
    {
        ArgumentNullException.ThrowIfNull(sign);
        if (!connection.IsAssigned ||
            !endpoints.TryGetValue(connection.Source, out Endpoint? endpoint) ||
            !endpoint.IsPlaying(connection.Player) ||
            !TryCreateState(sign, connection.Player.Slot.Value, flags: 0, out TerrariaSignState state))
        {
            return false;
        }

        byte[] encoded;
        try
        {
            encoded = TerrariaSignCodec.EncodeState(in state);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or EncoderFallbackException)
        {
            RejectedFrames++;
            return false;
        }

        if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) != OutboundEnqueueResult.Enqueued)
        {
            RejectedFrames++;
            return false;
        }

        ReadFrames++;
        return true;
    }

    public void PublishChanged(ConnectionHandle source, WorldSign sign, byte flags)
    {
        ArgumentNullException.ThrowIfNull(sign);
        if (!source.IsAssigned ||
            !TryCreateState(sign, source.Player.Slot.Value, flags, out TerrariaSignState state))
        {
            RejectedFrames++;
            return;
        }

        byte[] encoded;
        try
        {
            encoded = TerrariaSignCodec.EncodeState(in state);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or EncoderFallbackException)
        {
            RejectedFrames++;
            return;
        }

        var frame = new OutboundFrame(encoded);
        foreach ((GameCommandSourceId endpointSource, Endpoint endpoint) in endpoints)
        {
            if (endpointSource == source.Source || !endpoint.IsPlaying())
                continue;

            if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                UpdateFrames++;
            else
                RejectedFrames++;
        }
    }

    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
    {
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
    }

    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        if (endpoints.TryGetValue(connection.Source, out Endpoint? endpoint))
            endpoint.MarkPlaying(connection.Player);
    }

    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        if (endpoints.TryGetValue(connection.Source, out Endpoint? endpoint))
            endpoint.ClearPlaying(connection.Player);
    }

    private static bool TryCreateState(
        WorldSign sign,
        byte player,
        byte flags,
        out TerrariaSignState state)
    {
        if (sign.SlotId < 0 ||
            sign.X < short.MinValue || sign.X > short.MaxValue ||
            sign.Y < short.MinValue || sign.Y > short.MaxValue)
        {
            state = default;
            return false;
        }

        state = new TerrariaSignState(
            sign.SlotId,
            checked((short)sign.X),
            checked((short)sign.Y),
            sign.Text,
            player,
            flags);
        return true;
    }

    private sealed class Endpoint(TerrariaConnectionOutboundQueue outbound)
    {
        private readonly object gate = new();
        private PlayerHandle? player;

        public TerrariaConnectionOutboundQueue Outbound { get; } = outbound;

        public void MarkPlaying(PlayerHandle value)
        {
            lock (gate)
                player = value;
        }

        public void ClearPlaying(PlayerHandle expected)
        {
            lock (gate)
            {
                if (player == expected)
                    player = null;
            }
        }

        public bool IsPlaying()
        {
            lock (gate)
                return player.HasValue;
        }

        public bool IsPlaying(PlayerHandle expected)
        {
            lock (gate)
                return player == expected;
        }
    }
}
