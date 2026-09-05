using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum TileManipulationFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedManipulation = 2,
    GameIngressBackpressure = 3
}

/// <summary>
/// Connection-owned packet-17 ingress. Exact wire decoding happens on the socket thread, but the request never
/// mutates tiles there. Playing-session identity is attached and the immutable request is posted to the bounded
/// authoritative command queue where gameplay authority can be decided safely.
/// </summary>
public sealed class TileManipulationFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly ITileNetworkIngress ingress;

    internal TileManipulationFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        ITileNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Tile ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ingress);

        this.source = source;
        this.bootstrap = bootstrap;
        this.inner = inner;
        this.ingress = ingress;
    }

    public TileManipulationFrameStopReason StopReason { get; private set; }

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        TileManipulationFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        TileManipulationFrameStopReason.MalformedManipulation => TerrariaFrameRejectionCategory.MalformedProtocol,
        TileManipulationFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => inner is ITerrariaFrameRejectionSource source
            ? source.RejectionCategory
            : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != TileManipulationFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        TerrariaMessageId messageId = (TerrariaMessageId)frame.MessageId;
        if (messageId is not (TerrariaMessageId.TileManipulation or TerrariaMessageId.LiquidSet))
            return inner.OnFrame(in frame);

        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(TileManipulationFrameStopReason.InvalidJoinState);

        if (messageId == TerrariaMessageId.LiquidSet)
        {
            TerrariaLiquidDecodeResult liquidDecode = TerrariaLiquidCodec.TryDecode(in frame, out TerrariaLiquidState liquid);
            if (liquidDecode != TerrariaLiquidDecodeResult.Decoded)
                return Stop(TileManipulationFrameStopReason.MalformedManipulation);

            // Packet 48 is replaceable state and can be spammed by bucket/pump exploits.  When the bounded
            // game ingress is full we keep the connection and discard the stale proposal instead of allowing
            // liquid traffic to turn queue pressure into a disconnect primitive.
            _ = ingress.TryPostLiquid(connection, in liquid);
            return TerrariaFrameSinkResult.Continue;
        }

        TerrariaTileManipulationDecodeResult decode = TerrariaTileManipulationCodec.TryDecode(
            in frame,
            out TerrariaTileManipulationState state);
        if (decode != TerrariaTileManipulationDecodeResult.Decoded)
            return Stop(TileManipulationFrameStopReason.MalformedManipulation);

        return ingress.TryPost(connection, in state)
            ? TerrariaFrameSinkResult.Continue
            : Stop(TileManipulationFrameStopReason.GameIngressBackpressure);
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

    private TerrariaFrameSinkResult Stop(TileManipulationFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
