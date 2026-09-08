using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum WorldItemFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedDrop = 2,
    MalformedOwner = 3,
    PlayerOwnershipMismatch = 4,
    MalformedRelease = 5,
}

/// <summary>
/// Connection-owned packet-21/39 ingress for client-originated world-item proposals. Mutation is accepted
/// only for a fully playing session; packet identities are decoded by the protocol adapter and converted into
/// packet-neutral Core updates before bounded authoritative queue admission. TerrariaServer 1.4.5.8 handles packet
/// 22 only when <c>Main.netMode != 2</c>, so inbound packet 22 is deliberately a server-side no-op. Runtime packet-22
/// replication remains an outbound projection of authoritative owner/reservation state. Packet39 releases only
/// the exact current owner's item; it cannot supply a new owner or coordinates.
/// </summary>
public sealed class WorldItemFrameSink :
    ITerrariaFrameSink,
    ITerrariaFrameRejectionSource,
    ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId _source;
    private readonly PlayerBootstrapFrameSink _bootstrap;
    private readonly ITerrariaFrameSink _inner;
    private readonly IWorldItemIngress _ingress;

    public WorldItemFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IWorldItemIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("World-item ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ingress);

        _source = source;
        _bootstrap = bootstrap;
        _inner = inner;
        _ingress = ingress;
    }

    public WorldItemFrameStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == WorldItemFrameStopReason.None && _inner is ITerrariaConnectionStopReasonSource source
            ? source.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        WorldItemFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        WorldItemFrameStopReason.MalformedDrop or WorldItemFrameStopReason.MalformedOwner or WorldItemFrameStopReason.MalformedRelease => TerrariaFrameRejectionCategory.MalformedProtocol,
        WorldItemFrameStopReason.PlayerOwnershipMismatch => TerrariaFrameRejectionCategory.GameplayRejected,
        _ => _inner is ITerrariaFrameRejectionSource source
            ? source.RejectionCategory
            : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != WorldItemFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        return (TerrariaMessageId)frame.MessageId switch
        {
            TerrariaMessageId.WorldItemDrop => HandleDrop(frame),
            TerrariaMessageId.ReleaseWorldItem => HandleRelease(frame),
            // TerrariaServer 1.4.5.8 MessageBuffer case 22 is guarded by Main.netMode != 2. The server therefore
            // neither decodes nor applies client packet 22. Mirroring that no-op also removes a client-authoritative
            // owner/reservation mutation path; packet 22 remains valid for server-to-client replication.
            TerrariaMessageId.WorldItemOwner => TerrariaFrameSinkResult.Continue,
            _ => _inner.OnFrame(in frame)
        };
    }

    private TerrariaFrameSinkResult HandleDrop(in TerrariaFrame frame)
    {
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(WorldItemFrameStopReason.InvalidJoinState);

        TerrariaWorldItemDropDecodeResult decode = TerrariaWorldItemDropDecoder.TryDecode(
            in frame,
            out TerrariaWorldItemDropState drop);
        if (decode != TerrariaWorldItemDropDecodeResult.Decoded)
            return Stop(WorldItemFrameStopReason.MalformedDrop);

        if (drop.IsNewItemRequest && drop.IsRemoval)
            return Stop(WorldItemFrameStopReason.MalformedDrop);

        if (drop.IsRemoval)
        {
            // Removal is a discrete proposal. Saturation leaves the authoritative item in place and keeps the
            // playing socket alive; normal replication can converge the client afterwards.
            _ = _ingress.TryPostRemove(connection, drop.ItemIndex);
            return TerrariaFrameSinkResult.Continue;
        }

        var state = new WorldItemDropStateUpdate(
            drop.PositionX,
            drop.PositionY,
            drop.VelocityX,
            drop.VelocityY,
            drop.Stack,
            drop.Prefix,
            (WorldItemOwnershipMode)(byte)drop.Ownership,
            drop.ItemNetId,
            drop.Shimmered,
            drop.ShimmerTime,
            drop.EnemyGrabDelayTime);

        bool posted = drop.IsNewItemRequest
            ? _ingress.TryPostAllocate(connection, in state)
            : _ingress.TryPostDrop(connection, drop.ItemIndex, in state);
        // Allocation/update is fail-closed on mailbox saturation: no authoritative item mutation occurs, but
        // internal queue pressure is not grounds to disconnect a valid playing client.
        _ = posted;
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult HandleRelease(in TerrariaFrame frame)
    {
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(WorldItemFrameStopReason.InvalidJoinState);
        if (!TerrariaWorldItemReleaseDecoder1458.TryDecode(in frame, out short slot, out bool forceServer))
            return Stop(WorldItemFrameStopReason.MalformedRelease);
        _ = _ingress.TryPostRelease(connection, slot, forceServer);
        return TerrariaFrameSinkResult.Continue;
    }

    private bool TryGetPlayingConnection(out ConnectionHandle connection)
    {
        if (_bootstrap.JoinState == PlayerJoinState.Playing &&
            _bootstrap.AssignedPlayerHandle is PlayerHandle player)
        {
            connection = new ConnectionHandle(_source, player);
            return true;
        }

        connection = default;
        return false;
    }

    private TerrariaFrameSinkResult Stop(WorldItemFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
