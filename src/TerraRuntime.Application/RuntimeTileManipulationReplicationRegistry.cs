using System.Collections.Concurrent;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Relays accepted packet-17 tile effects, committed packet-79 object placements and server-authored packet-19
/// door state transitions to playing peers. Client-originated effects exclude their source; authoritative NPC door
/// transitions fan out to every playing endpoint. Join baselines are intentionally omitted because packet 10 carries
/// authoritative tile state.
/// </summary>
internal sealed class RuntimeTileManipulationReplicationRegistry : IRuntimePlayerEventSink
{
    // A liquid simulation slice may touch 2,500 cells. Never turn that implementation budget directly
    // into a client-controlled outbound burst: retain the latest state per cell and drain a small, fixed
    // transport budget on the authoritative world loop. Section resends remain the eventual full-state path.
    internal const int MaxLiquidFramesPerAuthoritativeTick = 16;
    internal const int MaxPendingLiquidUpdates = 16 * 1024;

    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();
    private readonly Dictionary<int, TerrariaLiquidState> pendingLiquids = [];
    private readonly Queue<int> pendingLiquidOrder = new();
    private long relayedFrames;
    private long rejectedFrames;
    private long encodeFailures;
    private long emittedLiquidUpdates;
    private long coalescedLiquidUpdates;
    private long droppedLiquidUpdates;

    public long RelayedFrames => Interlocked.Read(ref relayedFrames);
    public long RejectedFrames => Interlocked.Read(ref rejectedFrames);
    public long EncodeFailures => Interlocked.Read(ref encodeFailures);

    public int PendingLiquidUpdates => pendingLiquids.Count;

    public long EmittedLiquidUpdates => Interlocked.Read(ref emittedLiquidUpdates);

    public long CoalescedLiquidUpdates => Interlocked.Read(ref coalescedLiquidUpdates);

    public long DroppedLiquidUpdates => Interlocked.Read(ref droppedLiquidUpdates);

    public bool TryRegister(GameCommandSourceId source, TerrariaConnectionOutboundQueue outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        return !source.IsSystem && endpoints.TryAdd(source, new Endpoint(outbound));
    }

    public bool TryUnregister(GameCommandSourceId source) => endpoints.TryRemove(source, out _);

    public bool TryPublishCommitted(
        GameCommandSourceId excludedSource,
        in TerrariaTileManipulationState state) =>
        TryPublishAccepted(excludedSource, in state);

    public bool TryPublishAccepted(
        GameCommandSourceId excludedSource,
        in TerrariaTileManipulationState state)
    {
        if (TerrariaTileManipulationCodec.TryEncode(in state, out byte[] encoded) !=
            TerrariaTileManipulationEncodeResult.Encoded)
        {
            Interlocked.Increment(ref encodeFailures);
            return false;
        }

        return TryPublishFrame(excludedSource, encoded);
    }

    public bool TryPublishAuthoritativeCorrection(
        GameCommandSourceId source,
        WorldTileStore tiles,
        int tileX,
        int tileY)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (source.IsSystem ||
            !TerrariaTileSquareCodec.TryEncodeCorrection(tiles, tileX, tileY, out byte[] encoded))
        {
            Interlocked.Increment(ref encodeFailures);
            return false;
        }

        if (!endpoints.TryGetValue(source, out Endpoint? endpoint) || !endpoint.IsPlaying)
            return false;

        Publish(endpoint, new OutboundFrame(encoded));
        return true;
    }

    /// <summary>
    /// Publishes a server-authored 3x3 authoritative tile square around a runtime material mutation.
    /// Liquid merge reactions are only admitted at the one-tile world margin, matching vanilla
    /// <c>Liquid.LiquidCheck</c>, so the exact surrounding 3x3 square is always representable.
    /// </summary>
    public bool TryPublishTileSquareToAll(WorldTileStore tiles, int tileX, int tileY) =>
        TryPublishTileSquareToAll(
            tiles,
            tileX - 1,
            tileY - 1,
            width: 3,
            height: 3,
            VanillaTileChangeType1458.None);

    public bool TryPublishTileSquareToAll(
        WorldTileStore tiles,
        int startX,
        int startY,
        byte width,
        byte height,
        VanillaTileChangeType1458 changeType)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!TerrariaTileSquareCodec.TryEncode(
                tiles,
                startX,
                startY,
                width,
                height,
                changeType,
                out byte[] encoded))
        {
            Interlocked.Increment(ref encodeFailures);
            return false;
        }

        return TryPublishFrameToAll(encoded);
    }

    public bool TryPublishPlaceObject(
        GameCommandSourceId excludedSource,
        in TerrariaPlaceObjectState state)
    {
        if (TerrariaPlaceObjectCodec.TryEncode(in state, out byte[] encoded) !=
            TerrariaPlaceObjectEncodeResult.Encoded)
        {
            Interlocked.Increment(ref encodeFailures);
            return false;
        }

        return TryPublishFrame(excludedSource, encoded);
    }

    public bool TryPublishLiquidToAll(in TerrariaLiquidState state)
    {
        int key = ((ushort)state.TileX << 16) | (ushort)state.TileY;
        if (pendingLiquids.TryGetValue(key, out TerrariaLiquidState existing))
        {
            pendingLiquids[key] = state;
            if (existing != state)
                Interlocked.Increment(ref coalescedLiquidUpdates);
            return true;
        }

        if (pendingLiquids.Count == MaxPendingLiquidUpdates)
        {
            int evictedKey = pendingLiquidOrder.Dequeue();
            _ = pendingLiquids.Remove(evictedKey);
            Interlocked.Increment(ref droppedLiquidUpdates);
        }

        pendingLiquids.Add(key, state);
        pendingLiquidOrder.Enqueue(key);
        return true;
    }

    /// <summary>
    /// Drains the fixed packet-48 transport budget. The sole caller is the authoritative world loop, after its
    /// liquid simulation slice has committed; no socket callback can advance, clear or reorder this queue.
    /// </summary>
    public void FlushPendingLiquids()
    {
        int budget = MaxLiquidFramesPerAuthoritativeTick;
        while (budget-- > 0 && pendingLiquidOrder.TryDequeue(out int key))
        {
            TerrariaLiquidState state = pendingLiquids[key];
            _ = pendingLiquids.Remove(key);
            if (!TerrariaLiquidCodec.TryEncode(in state, out byte[] encoded))
            {
                Interlocked.Increment(ref encodeFailures);
                continue;
            }

            _ = TryPublishFrameToAll(encoded);
            Interlocked.Increment(ref emittedLiquidUpdates);
        }
    }

    public bool TryPublishDoorToggle(in TerrariaDoorToggleState state)
    {
        if (TerrariaDoorToggleCodec.TryEncode(in state, out byte[] encoded) !=
            TerrariaDoorToggleEncodeResult.Encoded)
        {
            Interlocked.Increment(ref encodeFailures);
            return false;
        }

        return TryPublishFrameToAll(encoded);
    }

    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        if (connection.Player.Slot == request.ClaimedSlot &&
            endpoints.TryGetValue(connection.Source, out Endpoint? endpoint))
        {
            endpoint.MarkPlaying(connection.Player);
        }
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        if (endpoints.TryGetValue(connection.Source, out Endpoint? endpoint))
            endpoint.ClearPlaying(connection.Player);
    }

    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) { }
    public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request) { }
    public void PlayerManaUpdated(ConnectionHandle connection, in PlayerManaCommitRequest request) { }
    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }

    private bool TryPublishFrame(GameCommandSourceId excludedSource, byte[] encoded)
    {
        var frame = new OutboundFrame(encoded);
        foreach ((GameCommandSourceId source, Endpoint endpoint) in endpoints)
        {
            if (source == excludedSource || !endpoint.IsPlaying)
                continue;

            Publish(endpoint, frame);
        }

        return true;
    }

    private bool TryPublishFrameToAll(byte[] encoded)
    {
        var frame = new OutboundFrame(encoded);
        foreach (Endpoint endpoint in endpoints.Values)
        {
            if (!endpoint.IsPlaying)
                continue;

            Publish(endpoint, frame);
        }

        return true;
    }

    private void Publish(Endpoint endpoint, in OutboundFrame frame)
    {
        if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
            Interlocked.Increment(ref relayedFrames);
        else
            Interlocked.Increment(ref rejectedFrames);
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
