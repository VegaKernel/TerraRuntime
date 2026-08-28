using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Network-side projection/cache for authoritative projectile commits. Active projectile baselines are
/// retained as packet-27 frames and emitted only after a connection completes the authoritative player
/// spawn transition. Packed ProjectileKey details remain confined to the protocol adapter boundary.
/// </summary>
internal sealed class RuntimeProjectileReplicationRegistry : IProjectileStateCommitSink, IRuntimePlayerEventSink
{
    private const int MaxProjectileSlots = RuntimeProjectileStore.MaximumProtocolAddressableCapacity;

    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();
    private readonly byte[]?[] baselineFrames = new byte[MaxProjectileSlots][];
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

    public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        if (kind == ProjectileStateCommitKind.Despawn)
        {
            if (!RuntimeProjectilePacketProjection.TryCreateDestroy(in snapshot, out var destroyState) ||
                !TerrariaProjectileEncoder.TryEncodeDestroy(in destroyState, out byte[] destroyFrame))
            {
                Interlocked.Increment(ref unsupportedCommits);
                return;
            }

            Broadcast(destroyFrame);
            Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], null);
            return;
        }

        if (kind is not ProjectileStateCommitKind.Spawn and not ProjectileStateCommitKind.Update)
            throw new ArgumentOutOfRangeException(nameof(kind));

        if (!RuntimeProjectilePacketProjection.TryCreateUpdate(in snapshot, out var updateState) ||
            !TerrariaProjectileEncoder.TryEncodeUpdate(in updateState, out byte[] updateFrame))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }

        Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], updateFrame);
        Broadcast(updateFrame);
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
