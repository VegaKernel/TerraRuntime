using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum ObjectPlacementFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedPlacement = 2,
    GameIngressBackpressure = 3
}

/// <summary>
/// Connection-owned packet-79 ingress. The socket thread performs only exact wire decoding and playing-session
/// attachment. Item ownership, object/style authorization, support checks, chest metadata, inventory consumption
/// and replication are authoritative-thread responsibilities.
/// </summary>
public sealed class ObjectPlacementFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IObjectPlacementNetworkIngress ingress;

    internal ObjectPlacementFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IObjectPlacementNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Object placement ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ingress);

        this.source = source;
        this.bootstrap = bootstrap;
        this.inner = inner;
        this.ingress = ingress;
    }

    public ObjectPlacementFrameStopReason StopReason { get; private set; }

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        ObjectPlacementFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        ObjectPlacementFrameStopReason.MalformedPlacement => TerrariaFrameRejectionCategory.MalformedProtocol,
        ObjectPlacementFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => inner is ITerrariaFrameRejectionSource source
            ? source.RejectionCategory
            : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != ObjectPlacementFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.PlaceObject)
            return inner.OnFrame(in frame);

        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(ObjectPlacementFrameStopReason.InvalidJoinState);

        TerrariaPlaceObjectDecodeResult decode = TerrariaPlaceObjectCodec.TryDecode(
            in frame,
            out TerrariaPlaceObjectState state);
        if (decode != TerrariaPlaceObjectDecodeResult.Decoded)
            return Stop(ObjectPlacementFrameStopReason.MalformedPlacement);

        return ingress.TryPost(connection, in state)
            ? TerrariaFrameSinkResult.Continue
            : Stop(ObjectPlacementFrameStopReason.GameIngressBackpressure);
    }

    private bool TryGetPlayingConnection(out ConnectionHandle connection)
    {
        if (bootstrap.JoinState == PlayerJoinState.Playing &&
            bootstrap.AssignedPlayerHandle is PlayerHandle player)
        {
            connection = new ConnectionHandle(source, player);
            return true;
        }

        connection = default;
        return false;
    }

    private TerrariaFrameSinkResult Stop(ObjectPlacementFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
