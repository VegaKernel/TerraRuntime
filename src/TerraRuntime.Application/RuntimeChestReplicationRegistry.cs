using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Transport-only projection of authoritative chest commits. The game thread decides ownership and state first;
/// this registry only serializes committed snapshots and fans them out to connection-owned bounded queues.
/// </summary>
internal sealed class RuntimeChestReplicationRegistry : IRuntimePlayerEventSink
{
    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();

    public long OpenFrames { get; private set; }
    public long ItemFrames { get; private set; }
    public long NameFrames { get; private set; }
    public long ChestIndexFrames { get; private set; }
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

    public bool TrySendOpen(ConnectionHandle connection, WorldChest chest)
    {
        if (!connection.IsAssigned ||
            !endpoints.TryGetValue(connection.Source, out Endpoint? endpoint) ||
            !endpoint.IsPlaying(connection.Player))
        {
            return false;
        }

        WorldChestSyncPacketEncodeResult encode = WorldChestSyncPacketEncoder.TryEncode(chest, out ReadOnlyMemory<byte>[] frames);
        if (encode != WorldChestSyncPacketEncodeResult.Encoded)
            return false;

        foreach (ReadOnlyMemory<byte> frame in frames)
        {
            if (endpoint.Outbound.TryEnqueue(new OutboundFrame(frame)) != OutboundEnqueueResult.Enqueued)
            {
                RejectedFrames++;
                return false;
            }
            OpenFrames++;
        }

        byte[] activeChest = TerrariaChestCodec.EncodeActiveChest(
            chest.SlotId,
            checked((short)chest.X),
            checked((short)chest.Y));
        if (endpoint.Outbound.TryEnqueue(new OutboundFrame(activeChest)) != OutboundEnqueueResult.Enqueued)
        {
            RejectedFrames++;
            return false;
        }
        OpenFrames++;

        BroadcastChestIndex(
            connection.Player.Slot.Value,
            chest.SlotId,
            excludedSource: connection.Source);
        return true;
    }

    public void PublishItem(ConnectionHandle source, in TerrariaChestItemState state)
    {
        byte[] encoded = TerrariaChestCodec.EncodeChestItem(in state);
        var frame = new OutboundFrame(encoded);
        foreach ((GameCommandSourceId endpointSource, Endpoint endpoint) in endpoints)
        {
            if (endpointSource == source.Source || !endpoint.IsPlaying())
                continue;

            if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                ItemFrames++;
            else
                RejectedFrames++;
        }
    }

    public void PublishRenamed(ConnectionHandle source, WorldChest chest)
    {
        ArgumentNullException.ThrowIfNull(chest);
        OutboundFrame frame = EncodeNameFrame(chest);

        foreach ((GameCommandSourceId endpointSource, Endpoint endpoint) in endpoints)
        {
            if (endpointSource == source.Source || !endpoint.IsPlaying())
                continue;

            if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                NameFrames++;
            else
                RejectedFrames++;
        }
    }

    public bool TrySendName(ConnectionHandle connection, WorldChest chest)
    {
        ArgumentNullException.ThrowIfNull(chest);
        if (!connection.IsAssigned ||
            !endpoints.TryGetValue(connection.Source, out Endpoint? endpoint) ||
            !endpoint.IsPlaying(connection.Player))
        {
            return false;
        }

        if (endpoint.Outbound.TryEnqueue(EncodeNameFrame(chest)) != OutboundEnqueueResult.Enqueued)
        {
            RejectedFrames++;
            return false;
        }

        NameFrames++;
        return true;
    }

    public void PublishClosed(ConnectionHandle connection) =>
        BroadcastChestIndex(
            connection.Player.Slot.Value,
            chestId: -1,
            excludedSource: connection.Source);

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

    private static OutboundFrame EncodeNameFrame(WorldChest chest) =>
        new(TerrariaChestCodec.EncodeChestName(
            chest.SlotId,
            checked((short)chest.X),
            checked((short)chest.Y),
            chest.Name));

    private void BroadcastChestIndex(byte playerSlot, short chestId, GameCommandSourceId excludedSource)
    {
        byte[] encoded = TerrariaChestCodec.EncodePlayerChestIndex(playerSlot, chestId);
        var frame = new OutboundFrame(encoded);
        foreach ((GameCommandSourceId source, Endpoint endpoint) in endpoints)
        {
            if (source == excludedSource || !endpoint.IsPlaying())
                continue;

            if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                ChestIndexFrames++;
            else
                RejectedFrames++;
        }
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
