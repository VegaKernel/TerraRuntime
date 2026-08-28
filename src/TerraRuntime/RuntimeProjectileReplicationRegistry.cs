using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Network-side projection/cache for authoritative projectile commits. Active projectile baselines are
/// retained as packet-27 frames and emitted only after a connection completes the authoritative player
/// spawn transition. Exact inbound ProjectileKeys are retained independently from physical runtime slots;
/// runtime-created projectiles receive a canonical fallback key only when no wire identity is registered.
/// Client-originated authoritative commits preserve their exact key and are relayed to every playing peer
/// except the source connection, matching vanilla server packet-27/29 echo suppression.
/// </summary>
internal sealed class RuntimeProjectileReplicationRegistry : IProjectileStateCommitSink, IRuntimePlayerEventSink
{
    private const int MaxProjectileSlots = RuntimeProjectileStore.MaximumProtocolAddressableCapacity;

    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();
    private readonly byte[]?[] baselineFrames = new byte[MaxProjectileSlots][];
    private readonly RuntimeProjectileWireIdentityRegistry identities;
    private readonly RuntimeProjectileClientCommitContext clientCommits;
    private long relayedFrames;
    private long baselineFrameCount;
    private long rejectedFrames;
    private long unsupportedCommits;

    public RuntimeProjectileReplicationRegistry()
        : this(new RuntimeProjectileWireIdentityRegistry(), new RuntimeProjectileClientCommitContext())
    {
    }

    internal RuntimeProjectileReplicationRegistry(RuntimeProjectileWireIdentityRegistry identities)
        : this(identities, new RuntimeProjectileClientCommitContext())
    {
    }

    internal RuntimeProjectileReplicationRegistry(
        RuntimeProjectileWireIdentityRegistry identities,
        RuntimeProjectileClientCommitContext clientCommits)
    {
        this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
        this.clientCommits = clientCommits ?? throw new ArgumentNullException(nameof(clientCommits));
    }

    public long RelayedFrames => Interlocked.Read(ref relayedFrames);

    public long BaselineFrames => Interlocked.Read(ref baselineFrameCount);

    public long RejectedFrames => Interlocked.Read(ref rejectedFrames);

    public long UnsupportedCommits => Interlocked.Read(ref unsupportedCommits);

    internal RuntimeProjectileWireIdentityRegistry WireIdentities => identities;

    internal RuntimeProjectileClientCommitContext ClientCommitContext => clientCommits;

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
        bool clientCommit = clientCommits.TryGet(
            out GameCommandSourceId excludedSource,
            out TerrariaProjectileKeyState clientKey);

        if (clientCommit && clientKey.Spawner != snapshot.Spawner)
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }

        if (kind == ProjectileStateCommitKind.Despawn)
        {
            try
            {
                TerrariaProjectileKeyState destroyKey;
                if (clientCommit)
                {
                    if (!identities.TryResolve(in clientKey, out ProjectileHandle resolved) ||
                        resolved != snapshot.Handle)
                    {
                        Interlocked.Increment(ref unsupportedCommits);
                        return;
                    }

                    destroyKey = clientKey;
                }
                else if (!TryResolveWireKey(in snapshot, out destroyKey))
                {
                    Interlocked.Increment(ref unsupportedCommits);
                    return;
                }

                if (!RuntimeProjectilePacketProjection.TryCreateDestroy(in snapshot, in destroyKey, out var destroyState) ||
                    !TerrariaProjectileEncoder.TryEncodeDestroy(in destroyState, out byte[] destroyFrame))
                {
                    Interlocked.Increment(ref unsupportedCommits);
                    return;
                }

                Broadcast(destroyFrame, clientCommit, excludedSource);
            }
            finally
            {
                Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], null);
                identities.TryUnbind(snapshot.Handle, out _);
            }

            return;
        }

        if (kind is not ProjectileStateCommitKind.Spawn and not ProjectileStateCommitKind.Update)
            throw new ArgumentOutOfRangeException(nameof(kind));

        TerrariaProjectileKeyState updateKey;
        if (clientCommit)
        {
            if (kind == ProjectileStateCommitKind.Spawn)
            {
                if (!identities.TryBind(in clientKey, snapshot.Handle))
                {
                    Interlocked.Increment(ref unsupportedCommits);
                    return;
                }
            }
            else if (!identities.TryResolve(in clientKey, out ProjectileHandle resolved) ||
                     resolved != snapshot.Handle)
            {
                Interlocked.Increment(ref unsupportedCommits);
                return;
            }

            updateKey = clientKey;
        }
        else if (!TryResolveOrCreateWireKey(in snapshot, out updateKey))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }

        if (!RuntimeProjectilePacketProjection.TryCreateUpdate(in snapshot, in updateKey, out var updateState) ||
            !TerrariaProjectileEncoder.TryEncodeUpdate(in updateState, out byte[] updateFrame))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }

        Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], updateFrame);
        Broadcast(updateFrame, clientCommit, excludedSource);
    }

    /// <summary>
    /// Relays a valid packet-29 state that did not resolve to an authoritative projectile. Vanilla still
    /// forwards this destroy notification to peers while excluding the sender, even though no local entity
    /// is mutated. This method must be called from the authoritative command path, not from the socket thread.
    /// </summary>
    internal bool TryRelayUnresolvedDestroy(
        GameCommandSourceId excludedSource,
        in TerrariaProjectileDestroyState state)
    {
        if (excludedSource.IsSystem || !state.IsValid ||
            !TerrariaProjectileEncoder.TryEncodeDestroy(in state, out byte[] frame))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return false;
        }

        Broadcast(frame, excludeSource: true, excludedSource);
        return true;
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

    private bool TryResolveWireKey(
        in ProjectileSnapshot snapshot,
        out TerrariaProjectileKeyState key) =>
        identities.TryGetWireKey(snapshot.Handle, out key) ||
        RuntimeProjectilePacketProjection.TryCreateCanonicalKey(in snapshot, out key);

    private bool TryResolveOrCreateWireKey(
        in ProjectileSnapshot snapshot,
        out TerrariaProjectileKeyState key)
    {
        if (identities.TryGetWireKey(snapshot.Handle, out key))
            return true;

        if (!RuntimeProjectilePacketProjection.TryCreateCanonicalKey(in snapshot, out key))
            return false;

        return identities.TryBind(in key, snapshot.Handle);
    }

    private void Broadcast(
        byte[] encoded,
        bool excludeSource = false,
        GameCommandSourceId excludedSource = default)
    {
        var frame = new OutboundFrame(encoded);
        foreach (KeyValuePair<GameCommandSourceId, Endpoint> pair in endpoints)
        {
            if (excludeSource && pair.Key == excludedSource)
                continue;

            Endpoint endpoint = pair.Value;
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
