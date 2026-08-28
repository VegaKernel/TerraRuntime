using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Relays authoritative packet-21/22 world-item commits to playing connections. Join baselines are intentionally
/// not cached here: PlayerBootstrapFrameSink captures the authoritative world-item store immediately before
/// entering the world, avoiding a second mutable shadow copy.
/// </summary>
internal sealed class RuntimeWorldItemReplicationRegistry : IWorldItemStateCommitSink, IRuntimePlayerEventSink
{
    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();
    private long relayedFrames;
    private long rejectedFrames;
    private long unsupportedCommits;

    public long RelayedFrames => Interlocked.Read(ref relayedFrames);

    public long RejectedFrames => Interlocked.Read(ref rejectedFrames);

    public long UnsupportedCommits => Interlocked.Read(ref unsupportedCommits);

    public bool TryRegister(GameCommandSourceId source, TerrariaConnectionOutboundQueue outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        if (source.IsSystem)
            return false;

        return endpoints.TryAdd(source, new Endpoint(outbound));
    }

    public bool TryUnregister(GameCommandSourceId source) => endpoints.TryRemove(source, out _);

    public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
    {
        ReadOnlyMemory<byte> encoded;
        TerrariaWorldItemFrameEncodeResult result = kind switch
        {
            WorldItemStateCommitKind.Drop => EncodeDrop(in snapshot, out encoded),
            WorldItemStateCommitKind.Owner => EncodeOwner(in snapshot, out encoded),
            WorldItemStateCommitKind.Remove => TerrariaWorldItemFrameEncoder.TryEncodeRemoval(
                snapshot.Handle.Slot,
                out encoded),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        if (result != TerrariaWorldItemFrameEncodeResult.Encoded)
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }

        Broadcast(encoded);
    }

    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        if (connection.Player.Slot != request.ClaimedSlot ||
            !endpoints.TryGetValue(connection.Source, out Endpoint? endpoint))
        {
            return;
        }

        endpoint.MarkPlaying(connection.Player);
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        if (endpoints.TryGetValue(connection.Source, out Endpoint? endpoint))
            endpoint.ClearPlaying(connection.Player);
    }

    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
    {
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
    }

    public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
    }

    public void PlayerManaUpdated(ConnectionHandle connection, in PlayerManaCommitRequest request)
    {
    }

    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
    }

    private static TerrariaWorldItemFrameEncodeResult EncodeDrop(
        in WorldItemSnapshot snapshot,
        out ReadOnlyMemory<byte> encoded)
    {
        var state = new TerrariaWorldItemDropState(
            ItemIndex: snapshot.Handle.Slot,
            PositionX: snapshot.PositionX,
            PositionY: snapshot.PositionY,
            VelocityX: snapshot.VelocityX,
            VelocityY: snapshot.VelocityY,
            Stack: snapshot.Stack,
            Prefix: snapshot.Prefix,
            ItemNetId: snapshot.ItemNetId,
            Ownership: (TerrariaWorldItemOwnership)(byte)snapshot.Ownership,
            Shimmered: snapshot.Shimmered,
            ShimmerTime: snapshot.ShimmerTime,
            EnemyGrabDelayTime: snapshot.EnemyGrabDelayTime);
        return TerrariaWorldItemFrameEncoder.TryEncodeDrop(in state, out encoded);
    }

    private static TerrariaWorldItemFrameEncodeResult EncodeOwner(
        in WorldItemSnapshot snapshot,
        out ReadOnlyMemory<byte> encoded)
    {
        var state = new TerrariaWorldItemOwnerState(
            ItemIndex: snapshot.Handle.Slot,
            OwnerPlayerId: snapshot.OwnerPlayerId,
            TimeToKeepReservation: snapshot.TimeToKeepReservation,
            GrabDelayPlayer: snapshot.GrabDelayPlayer,
            GrabDelayTime: snapshot.GrabDelayTime,
            PositionX: snapshot.PositionX,
            PositionY: snapshot.PositionY);
        return TerrariaWorldItemFrameEncoder.TryEncodeOwner(in state, out encoded);
    }

    private void Broadcast(ReadOnlyMemory<byte> encoded)
    {
        var frame = new OutboundFrame(encoded);
        foreach (Endpoint endpoint in endpoints.Values)
        {
            if (!endpoint.IsPlaying)
                continue;

            if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref relayedFrames);
            else
                Interlocked.Increment(ref rejectedFrames);
        }
    }

    private sealed class Endpoint(TerrariaConnectionOutboundQueue outbound)
    {
        private int playingSlot = -1;
        private ulong playingGeneration;

        public TerrariaConnectionOutboundQueue Outbound { get; } =
            outbound ?? throw new ArgumentNullException(nameof(outbound));

        public bool IsPlaying =>
            Volatile.Read(ref playingSlot) >= 0 && Volatile.Read(ref playingGeneration) != 0;

        public void MarkPlaying(PlayerHandle player)
        {
            Volatile.Write(ref playingGeneration, player.Generation.Value);
            Volatile.Write(ref playingSlot, player.Slot.Value);
        }

        public void ClearPlaying(PlayerHandle player)
        {
            if (Volatile.Read(ref playingGeneration) != player.Generation.Value ||
                Interlocked.CompareExchange(ref playingSlot, -1, player.Slot.Value) != player.Slot.Value)
            {
                return;
            }

            Volatile.Write(ref playingGeneration, 0);
        }
    }
}
