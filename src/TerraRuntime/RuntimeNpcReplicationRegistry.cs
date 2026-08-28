using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Network-side projection/cache for authoritative NPC commits. Active-slot baselines are stored in
/// spawn form so a joining client always resets the slot even when its wrapped byte generation happens
/// to match. Live commits are broadcast only to connections that completed the player spawn transition.
/// </summary>
internal sealed class RuntimeNpcReplicationRegistry : INpcStateCommitSink, IRuntimePlayerEventSink
{
    private const int MaxNpcSlots = RuntimeNpcStore.MaximumAddressableCapacity;

    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();
    private readonly byte[]?[] baselineFrames = new byte[MaxNpcSlots][];
    private long relayedFrames;
    private long baselineFrameCount;
    private long rejectedFrames;
    private long unsupportedCommits;

    public long RelayedFrames => Interlocked.Read(ref relayedFrames);

    public long BaselineFrames => Interlocked.Read(ref baselineFrameCount);

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

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        RuntimeNpcSyncKind syncKind = kind switch
        {
            NpcStateCommitKind.Spawn => RuntimeNpcSyncKind.Spawn,
            NpcStateCommitKind.Update => RuntimeNpcSyncKind.Update,
            NpcStateCommitKind.Despawn => RuntimeNpcSyncKind.Despawn,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        if (!RuntimeNpcPacketProjection.TryCreate(in snapshot, syncKind, out var state) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in state, out byte[] encoded))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }

        if (kind == NpcStateCommitKind.Despawn)
        {
            Broadcast(encoded);
            Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], null);
            return;
        }

        // Baselines deliberately use spawn semantics even after an ordinary update. The explicit
        // SpawnNeedsSyncing flag protects a joining client from byte-generation wrap aliasing.
        if (RuntimeNpcPacketProjection.TryCreate(
                in snapshot,
                RuntimeNpcSyncKind.Spawn,
                out var baselineState) &&
            TerrariaNpcUpdateEncoder.TryEncode(in baselineState, out byte[] baseline))
        {
            Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], baseline);
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
        for (int slot = 0; slot < baselineFrames.Length; slot++)
        {
            byte[]? encoded = Volatile.Read(ref baselineFrames[slot]);
            if (encoded is null)
                continue;

            if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref baselineFrameCount);
            else
                Interlocked.Increment(ref rejectedFrames);
        }
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

    private void Broadcast(byte[] encoded)
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
