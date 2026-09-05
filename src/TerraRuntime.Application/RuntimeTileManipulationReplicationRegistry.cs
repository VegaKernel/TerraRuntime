using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>
/// Relays accepted packet-17 tile effects, committed packet-79 object placements and server-authored packet-19
/// door state transitions to playing peers. Client-originated effects exclude their source; authoritative NPC door
/// transitions fan out to every playing endpoint. Join baselines are intentionally omitted because packet 10 carries
/// authoritative tile state.
/// </summary>
internal sealed class RuntimeTileManipulationReplicationRegistry : IRuntimePlayerEventSink
{
    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();
    private long relayedFrames;
    private long rejectedFrames;
    private long encodeFailures;

    public long RelayedFrames => Interlocked.Read(ref relayedFrames);
    public long RejectedFrames => Interlocked.Read(ref rejectedFrames);
    public long EncodeFailures => Interlocked.Read(ref encodeFailures);

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
