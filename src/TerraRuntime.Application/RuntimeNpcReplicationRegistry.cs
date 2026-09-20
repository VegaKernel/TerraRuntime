using System.Collections.Concurrent;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>
/// Network-side projection/cache for authoritative NPC commits. Active-slot baselines are stored in
/// spawn form so a joining client always resets the slot even when its wrapped byte generation happens
/// to match. Live commits are broadcast only to connections that completed the player spawn transition.
/// Exact packet-23 update duplicates are coalesced per full runtime generation before peer fanout.
/// Ordinary motion commits are additionally sampled on vanilla's default <c>Main.npcStreamSpeed</c>
/// cadence; spawn, despawn and committed life changes remain immediate.
/// </summary>
internal sealed class RuntimeNpcReplicationRegistry : INpcStateCommitSink, IRuntimePlayerEventSink, INpcAiHealingCommitSink, INpcAiTauntCommitSink
{
    private const int MaxNpcSlots = RuntimeNpcStore.MaximumAddressableCapacity;
    // TerrariaServer 1.4.5.8 Main.npcStreamSpeed defaults to 30. This is a containment boundary
    // until each supported AI path can express the source NPC.netUpdate intent explicitly.
    private const long MotionResyncTicks = 30;
    // TerrariaServer 1.4.5.8 Main.npcStreamSpeed and NPC.StreamUpdatesToNearbyPlayers().
    private const int MultiplayerProximityStreamTicks = 30;
    private const byte MultiplayerProximityStreamThreshold = 8;

    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();
    private readonly byte[]?[] baselineFrames = new byte[MaxNpcSlots][];
    private readonly byte[]?[] despawnFrames = new byte[MaxNpcSlots][];
    private readonly object liveFrameGate = new();
    private readonly NpcHandle[] liveFrameOwners = new NpcHandle[MaxNpcSlots];
    private readonly byte[]?[] liveFrames = new byte[MaxNpcSlots][];
    private readonly NpcSnapshot[] liveSnapshots = new NpcSnapshot[MaxNpcSlots];
    private readonly long[] liveFrameTicks = new long[MaxNpcSlots];
    private readonly object proximityStreamGate = new();
    private readonly NpcHandle[] proximityStreamOwners = new NpcHandle[MaxNpcSlots];
    private readonly int[] proximityStreamTicks = new int[MaxNpcSlots];
    private readonly byte[]?[] townHomeBaselineFrames = new byte[RuntimeTownNpcStateStore.MaximumTownNpcs][];
    private readonly byte[]?[] townIdentityBaselineFrames = new byte[RuntimeTownNpcStateStore.MaximumTownNpcs][];
    private long relayedFrames;
    private long baselineFrameCount;
    private long rejectedFrames;
    private long unsupportedCommits;
    private long suppressedDuplicateFrames;
    private long suppressedCadenceFrames;
    private long authoritativeTick;
    private NpcHandle suppressedClientDamageNpc;

    public long RelayedFrames => Interlocked.Read(ref relayedFrames);

    public long BaselineFrames => Interlocked.Read(ref baselineFrameCount);

    public long RejectedFrames => Interlocked.Read(ref rejectedFrames);

    public long UnsupportedCommits => Interlocked.Read(ref unsupportedCommits);

    public long SuppressedDuplicateFrames => Interlocked.Read(ref suppressedDuplicateFrames);

    public long SuppressedCadenceFrames => Interlocked.Read(ref suppressedCadenceFrames);

    /// <summary>
    /// Advances the replication clock from the authoritative game loop. Socket callbacks never invoke this.
    /// </summary>
    public void AdvanceAuthoritativeTick() => authoritativeTick++;

    public bool TryRegister(GameCommandSourceId source, TerrariaConnectionOutboundQueue outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        if (source.IsSystem)
            return false;

        return endpoints.TryAdd(source, new Endpoint(outbound));
    }

    public bool TryUnregister(GameCommandSourceId source) => endpoints.TryRemove(source, out _);

    public bool TryBeginClientDamage(NpcHandle npc)
    {
        if (!npc.IsAssigned || suppressedClientDamageNpc.IsAssigned)
            return false;
        suppressedClientDamageNpc = npc;
        return true;
    }

    public void CompleteClientDamage(NpcHandle npc)
    {
        if (suppressedClientDamageNpc != npc)
            throw new InvalidOperationException("Packet-28 replication scope does not match the completing NPC generation.");
        suppressedClientDamageNpc = default;
    }

    public void AbortClientDamage(NpcHandle npc)
    {
        if (suppressedClientDamageNpc == npc)
            suppressedClientDamageNpc = default;
    }

    public bool TryAcknowledgeDamage(GameCommandSourceId source)
    {
        if (!endpoints.TryGetValue(source, out Endpoint? endpoint) || !endpoint.IsPlaying ||
            TerrariaNpcDamageCodec.TryEncodeAck(out byte[] encoded) != TerrariaNpcDamageEncodeResult.Encoded)
        {
            return false;
        }

        if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
        {
            Interlocked.Increment(ref relayedFrames);
            return true;
        }

        Interlocked.Increment(ref rejectedFrames);
        return false;
    }

    public bool TryPublishDamage(GameCommandSourceId excludedSource, in TerrariaNpcDamageState state)
    {
        if (TerrariaNpcDamageCodec.TryEncode(in state, out byte[] encoded) != TerrariaNpcDamageEncodeResult.Encoded)
        {
            Interlocked.Increment(ref unsupportedCommits);
            return false;
        }

        BroadcastExcept(excludedSource, encoded);
        return true;
    }

    public bool TryPublishDeath(in NpcSnapshot snapshot)
    {
        if (!RuntimeNpcPacketProjection.TryCreate(in snapshot, RuntimeNpcSyncKind.Despawn, out TerrariaNpcUpdateState state) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in state, out byte[] encoded))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return false;
        }

        Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], null);
        Volatile.Write(ref despawnFrames[snapshot.Handle.Slot], null);
        ClearLiveFrame(snapshot.Handle);
        Broadcast(encoded);
        return true;
    }

    public void ConfigureTownHomeBaselines(ReadOnlySpan<RuntimeTownNpcHomeCommit> homes)
    {
        Array.Clear(townHomeBaselineFrames, 0, townHomeBaselineFrames.Length);
        foreach (RuntimeTownNpcHomeCommit home in homes)
        {
            if ((uint)home.NpcSlot >= (uint)townHomeBaselineFrames.Length)
                continue;
            TerrariaNpcHomeState state = home.ToWireState();
            if (TerrariaNpcHomeCodec.TryEncode(in state, out byte[] encoded) != TerrariaNpcHomeEncodeResult.Encoded)
                continue;
            Volatile.Write(ref townHomeBaselineFrames[home.NpcSlot], encoded);
        }
    }

    public void ConfigureTownIdentityBaselines(ReadOnlySpan<RuntimeTownNpcIdentityCommit> identities)
    {
        Array.Clear(townIdentityBaselineFrames, 0, townIdentityBaselineFrames.Length);
        foreach (RuntimeTownNpcIdentityCommit identity in identities)
        {
            if ((uint)identity.NpcSlot >= (uint)townIdentityBaselineFrames.Length)
                continue;
            TerrariaTownNpcIdentityState state = identity.ToWireState();
            if (TerrariaTownNpcIdentityCodec.TryEncode(in state, out byte[] encoded) != TerrariaTownNpcIdentityEncodeResult.Encoded)
                continue;
            Volatile.Write(ref townIdentityBaselineFrames[identity.NpcSlot], encoded);
        }
    }

    public bool TryPublishTownIdentity(in RuntimeTownNpcIdentityCommit identity)
    {
        if ((uint)identity.NpcSlot >= (uint)townIdentityBaselineFrames.Length)
            return false;
        TerrariaTownNpcIdentityState state = identity.ToWireState();
        if (TerrariaTownNpcIdentityCodec.TryEncode(in state, out byte[] encoded) != TerrariaTownNpcIdentityEncodeResult.Encoded)
            return false;
        Volatile.Write(ref townIdentityBaselineFrames[identity.NpcSlot], encoded);
        Broadcast(encoded);
        return true;
    }

    public bool TryPublishTownArrival(NpcTypeId npcType, string givenName)
    {
        ArgumentNullException.ThrowIfNull(givenName);
        if (!TerrariaTownNpcArrivalCodec1458.TryEncode(npcType.Value, givenName, out byte[] encoded))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return false;
        }
        Broadcast(encoded);
        return true;
    }

    public bool TryPublishNpcTalk(ConnectionHandle connection, short npcSlot)
    {
        if (!connection.IsAssigned || !TerrariaNpcTalkCodec.IsValidNpcSlot(npcSlot))
            return false;
        var state = new TerrariaNpcTalkState(connection.Player.Slot.Value, npcSlot);
        if (TerrariaNpcTalkCodec.TryEncode(in state, out byte[] encoded) != TerrariaNpcTalkEncodeResult.Encoded)
            return false;
        BroadcastExcept(connection.Source, encoded);
        return true;
    }

    public bool TryPublishTownHome(in RuntimeTownNpcHomeCommit home)
    {
        if ((uint)home.NpcSlot >= (uint)townHomeBaselineFrames.Length)
            return false;
        TerrariaNpcHomeState state = home.ToWireState();
        if (TerrariaNpcHomeCodec.TryEncode(in state, out byte[] encoded) != TerrariaNpcHomeEncodeResult.Encoded)
            return false;
        Volatile.Write(ref townHomeBaselineFrames[home.NpcSlot], encoded);
        Broadcast(encoded);
        return true;
    }

    public void NpcHealed(in NpcSnapshot npc, int amount)
    {
        if (amount <= 0 || !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(npc.Simulation, out var hitbox)) return;
        // AI82 passes Utils.CenteredRectangle(Center, new Vector2(50)) to NPC.HealEffect.
        float x = (int)(npc.PositionX + hitbox.Width * .5f - 25f) + 25;
        float y = (int)(npc.PositionY + hitbox.Height * .5f - 25f) + 25;
        if (RuntimeNpcPacketProjection.TryCreate(in npc, RuntimeNpcSyncKind.Spawn, out var baseline) &&
            TerrariaNpcUpdateEncoder.TryEncode(in baseline, out var encoded))
            Volatile.Write(ref baselineFrames[npc.Handle.Slot], encoded);
        Broadcast(TerrariaCombatTextCodec.EncodeNumber(x, y, amount, new TerrariaRgbColor(100, 255, 100)));
    }

    public void SkeletronTaunt(in NpcSnapshot source, int variant)
    {
        if (TerrariaSkeletronTauntCodec1458.TryEncode(variant, out var encoded)) Broadcast(encoded);
    }

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        RuntimeNpcSyncKind syncKind = kind switch
        {
            NpcStateCommitKind.Spawn => RuntimeNpcSyncKind.Spawn,
            NpcStateCommitKind.Update or NpcStateCommitKind.ForcedUpdate => RuntimeNpcSyncKind.Update,
            NpcStateCommitKind.Despawn => RuntimeNpcSyncKind.Despawn,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        if (!RuntimeNpcPacketProjection.TryCreate(in snapshot, syncKind, out var state) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in state, out byte[] encoded))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }

        bool suppressBroadcast = suppressedClientDamageNpc.IsAssigned && snapshot.Handle == suppressedClientDamageNpc;
        if (kind == NpcStateCommitKind.Despawn)
        {
            if (!suppressBroadcast)
                Broadcast(encoded);
            Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], null);
            Volatile.Write(ref despawnFrames[snapshot.Handle.Slot], null);
            ClearLiveFrame(snapshot.Handle);
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
        if (RuntimeNpcPacketProjection.TryCreate(
                in snapshot,
                RuntimeNpcSyncKind.Despawn,
                out var despawnState) &&
            TerrariaNpcUpdateEncoder.TryEncode(in despawnState, out byte[] despawn))
        {
            Volatile.Write(ref despawnFrames[snapshot.Handle.Slot], despawn);
        }

        if (suppressBroadcast)
            return;

        bool duplicate = IsDuplicateLiveFrame(snapshot.Handle, encoded);
        if (kind == NpcStateCommitKind.Update)
            StreamMoonLordUpdateToNearbyPlayers(in snapshot, encoded);
        if (kind == NpcStateCommitKind.Update && duplicate)
        {
            Interlocked.Increment(ref suppressedDuplicateFrames);
            return;
        }

        if (kind == NpcStateCommitKind.Update && ShouldSuppressForCadence(in snapshot))
        {
            Interlocked.Increment(ref suppressedCadenceFrames);
            return;
        }

        Broadcast(encoded);
        RecordLiveFrame(in snapshot, encoded);
    }

    private bool IsDuplicateLiveFrame(NpcHandle owner, byte[] encoded)
    {
        int slot = owner.Slot;
        lock (liveFrameGate)
        {
            byte[]? previous = liveFrames[slot];
            bool duplicate = liveFrameOwners[slot] == owner &&
                previous is not null &&
                previous.AsSpan().SequenceEqual(encoded);
            return duplicate;
        }
    }

    private bool ShouldSuppressForCadence(in NpcSnapshot snapshot)
    {
        long currentTick = authoritativeTick;
        if (currentTick == 0)
            return false;

        int slot = snapshot.Handle.Slot;
        lock (liveFrameGate)
        {
            if (liveFrameOwners[slot] != snapshot.Handle)
                return false;

            NpcSnapshot previous = liveSnapshots[slot];
            if (previous.Simulation.Life != snapshot.Simulation.Life ||
                previous.Simulation.LifeMax != snapshot.Simulation.LifeMax)
            {
                return false;
            }

            return currentTick - liveFrameTicks[slot] < MotionResyncTicks;
        }
    }

    private void RecordLiveFrame(in NpcSnapshot snapshot, byte[] encoded)
    {
        int slot = snapshot.Handle.Slot;
        lock (liveFrameGate)
        {
            liveFrameOwners[slot] = snapshot.Handle;
            liveFrames[slot] = encoded;
            liveSnapshots[slot] = snapshot;
            liveFrameTicks[slot] = authoritativeTick;
        }
    }

    private void ClearLiveFrame(NpcHandle owner)
    {
        int slot = owner.Slot;
        lock (liveFrameGate)
        {
            if (liveFrameOwners[slot] != owner)
                return;
            liveFrameOwners[slot] = default;
            liveFrames[slot] = null;
            liveSnapshots[slot] = default;
            liveFrameTicks[slot] = 0;
        }
    }

    /// <summary>
    /// Mirrors NPC.StreamUpdatesToNearbyPlayers for the complete 1.4.5.8 catalog entry
    /// NPCID.Sets.UsesMultiplayerProximitySyncing: Moon Lord head, hands and core (396-398).
    /// The regular packet-23 fallback remains separate because it models an older bounded transport
    /// policy rather than source NPC.netUpdate ownership.
    /// </summary>
    private void StreamMoonLordUpdateToNearbyPlayers(in NpcSnapshot snapshot, byte[] encoded)
    {
        if (!UsesMultiplayerProximitySyncing(snapshot.TypeIdentity) ||
            Math.Abs(snapshot.VelocityX) + Math.Abs(snapshot.VelocityY) <= .5f ||
            !TryAdvanceProximityStream(snapshot.Handle))
        {
            return;
        }

        if (!VanillaNpcDefinitionCatalog.TryGet(snapshot.TypeIdentity, snapshot.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(snapshot.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return;
        }

        float centerX = snapshot.PositionX + hitbox.Width * .5f;
        float centerY = snapshot.PositionY + hitbox.Height * .5f;
        var frame = new OutboundFrame(encoded);
        foreach (Endpoint endpoint in endpoints.Values)
        {
            if (!endpoint.TryAccumulateProximityStream(
                    snapshot.Handle,
                    centerX,
                    centerY,
                    definition.IsBoss,
                    MultiplayerProximityStreamThreshold))
            {
                continue;
            }

            if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref relayedFrames);
            else
                Interlocked.Increment(ref rejectedFrames);
        }
    }

    private bool TryAdvanceProximityStream(NpcHandle owner)
    {
        int slot = owner.Slot;
        lock (proximityStreamGate)
        {
            if (proximityStreamOwners[slot] != owner)
            {
                proximityStreamOwners[slot] = owner;
                proximityStreamTicks[slot] = 0;
            }

            proximityStreamTicks[slot]++;
            if (proximityStreamTicks[slot] < MultiplayerProximityStreamTicks)
                return false;

            proximityStreamTicks[slot] = 0;
            return true;
        }
    }

    private static bool UsesMultiplayerProximitySyncing(NpcTypeId type) =>
        type == VanillaNpcIds.MoonLordHead ||
        type == VanillaNpcIds.MoonLordHand ||
        type == VanillaNpcIds.MoonLordCore;

    internal ReadOnlyMemory<byte>[] CaptureWorldTransferDespawnFrames()
    {
        var result = new List<ReadOnlyMemory<byte>>();
        for (int slot = 0; slot < despawnFrames.Length; slot++)
        {
            byte[]? encoded = Volatile.Read(ref despawnFrames[slot]);
            if (encoded is not null)
                result.Add(encoded);
        }
        return result.ToArray();
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

        for (int slot = 0; slot < townIdentityBaselineFrames.Length; slot++)
        {
            byte[]? encoded = Volatile.Read(ref townIdentityBaselineFrames[slot]);
            if (encoded is null)
                continue;

            if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref baselineFrameCount);
            else
                Interlocked.Increment(ref rejectedFrames);
        }

        for (int slot = 0; slot < townHomeBaselineFrames.Length; slot++)
        {
            byte[]? encoded = Volatile.Read(ref townHomeBaselineFrames[slot]);
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
        if (connection.Player.Slot != request.PlayerSlot ||
            !endpoints.TryGetValue(connection.Source, out Endpoint? endpoint))
        {
            return;
        }

        endpoint.UpdateReportedCameraPosition(connection.Player, request);
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

    private void BroadcastExcept(GameCommandSourceId excludedSource, byte[] encoded)
    {
        var frame = new OutboundFrame(encoded);
        foreach ((GameCommandSourceId source, Endpoint endpoint) in endpoints)
        {
            if (source == excludedSource || !endpoint.IsPlaying)
                continue;

            if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                Interlocked.Increment(ref relayedFrames);
            else
                Interlocked.Increment(ref rejectedFrames);
        }
    }

    private sealed class Endpoint(TerrariaConnectionOutboundQueue outbound)
    {
        private readonly object proximityStreamGate = new();
        private readonly Dictionary<NpcHandle, byte> proximityStreamCounters = new();
        private int playingSlot = -1;
        private ulong playingGeneration;
        private float reportedCameraX;
        private float reportedCameraY;

        public TerrariaConnectionOutboundQueue Outbound { get; } =
            outbound ?? throw new ArgumentNullException(nameof(outbound));

        public bool IsPlaying =>
            Volatile.Read(ref playingSlot) >= 0 && Volatile.Read(ref playingGeneration) != 0;

        public void MarkPlaying(PlayerHandle player)
        {
            lock (proximityStreamGate)
            {
                proximityStreamCounters.Clear();
                reportedCameraX = 0f;
                reportedCameraY = 0f;
            }
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
            lock (proximityStreamGate)
                proximityStreamCounters.Clear();
        }

        public void UpdateReportedCameraPosition(PlayerHandle player, in PlayerMovementCommitRequest request)
        {
            if (!IsPlaying ||
                Volatile.Read(ref playingSlot) != player.Slot.Value ||
                Volatile.Read(ref playingGeneration) != player.Generation.Value)
            {
                return;
            }

            lock (proximityStreamGate)
            {
                reportedCameraX = request.HasCameraTarget ? request.CameraTargetX : request.PositionX;
                reportedCameraY = request.HasCameraTarget ? request.CameraTargetY : request.PositionY;
            }
        }

        public bool TryAccumulateProximityStream(
            NpcHandle npc,
            float npcCenterX,
            float npcCenterY,
            bool boss,
            byte threshold)
        {
            if (!IsPlaying)
                return false;

            lock (proximityStreamGate)
            {
                float dx = npcCenterX - reportedCameraX;
                float dy = npcCenterY - reportedCameraY;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                byte gain = boss
                    ? threshold
                    : distance < 250f ? threshold
                    : distance < 500f ? (byte)4
                    : distance < 1_000f ? (byte)2
                    : distance < 1_500f ? (byte)1
                    : (byte)0;
                byte counter = (byte)(proximityStreamCounters.GetValueOrDefault(npc) + gain);
                proximityStreamCounters[npc] = counter;
                return counter >= threshold;
            }
        }
    }
}
