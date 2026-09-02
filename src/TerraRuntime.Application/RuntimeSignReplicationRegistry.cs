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

    /// <summary>
    /// Reports whether the transport endpoint still represents this exact player-session generation. Authoritative
    /// command processors use this as a final stale-session gate before mutating sign state.
    /// </summary>
    public bool IsPlaying(ConnectionHandle connection) =>
        connection.IsAssigned &&
        endpoints.TryGetValue(connection.Source, out Endpoint? endpoint) &&
        endpoint.IsPlaying(connection.Player);

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

    public void PublishChanged(ConnectionHandle source, WorldSign sign)
    {
        ArgumentNullException.ThrowIfNull(sign);
        if (!source.IsAssigned ||
            !TryCreateState(sign, source.Player.Slot.Value, flags: 0, out TerrariaSignState state))
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
        private int playingSlot = -1;
        private ulong playingGeneration;

        public TerrariaConnectionOutboundQueue Outbound { get; } = outbound;

        public void MarkPlaying(PlayerHandle value)
        {
            Volatile.Write(ref playingGeneration, value.Generation.Value);
            Volatile.Write(ref playingSlot, value.Slot.Value);
        }

        public void ClearPlaying(PlayerHandle expected)
        {
            if (Volatile.Read(ref playingGeneration) != expected.Generation.Value ||
                Interlocked.CompareExchange(ref playingSlot, -1, expected.Slot.Value) != expected.Slot.Value)
            {
                return;
            }

            Volatile.Write(ref playingGeneration, 0);
        }

        public bool IsPlaying() =>
            Volatile.Read(ref playingSlot) >= 0 && Volatile.Read(ref playingGeneration) != 0;

        public bool IsPlaying(PlayerHandle expected) =>
            Volatile.Read(ref playingSlot) == expected.Slot.Value &&
            Volatile.Read(ref playingGeneration) == expected.Generation.Value;
    }
}
