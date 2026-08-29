using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

public enum SignInteractionFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedSignPacket = 2,
    GameIngressBackpressure = 3
}

/// <summary>
/// Connection-owned protocol-326 sign boundary for packets 46 and 47. The socket thread decodes only; every sign
/// lookup/update is posted with the exact playing-session identity to the authoritative game loop.
/// </summary>
public sealed class SignInteractionFrameSink :
    ITerrariaFrameSink,
    ITerrariaFrameRejectionSource,
    ITerrariaConnectionReadinessSource,
    ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly ISignNetworkIngress ingress;

    internal SignInteractionFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        ISignNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Sign ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ingress);

        this.source = source;
        this.bootstrap = bootstrap;
        this.inner = inner;
        this.ingress = ingress;
    }

    public SignInteractionFrameStopReason StopReason { get; private set; }

    public bool ConnectionReady => bootstrap.JoinState == PlayerJoinState.Playing;

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == SignInteractionFrameStopReason.None && inner is ITerrariaConnectionStopReasonSource source
            ? source.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        SignInteractionFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        SignInteractionFrameStopReason.MalformedSignPacket => TerrariaFrameRejectionCategory.MalformedProtocol,
        SignInteractionFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => inner is ITerrariaFrameRejectionSource source
            ? source.RejectionCategory
            : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != SignInteractionFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        TerrariaMessageId messageId = (TerrariaMessageId)frame.MessageId;
        if (messageId is not TerrariaMessageId.RequestSign and not TerrariaMessageId.SignNew)
            return inner.OnFrame(in frame);

        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(SignInteractionFrameStopReason.InvalidJoinState);

        bool posted;
        switch (messageId)
        {
            case TerrariaMessageId.RequestSign:
            {
                TerrariaSignDecodeResult decode = TerrariaSignCodec.TryDecodeReadRequest(
                    in frame,
                    out TerrariaSignReadRequest request);
                if (decode != TerrariaSignDecodeResult.Decoded)
                    return Stop(SignInteractionFrameStopReason.MalformedSignPacket);
                posted = ingress.TryPostRead(connection, in request);
                break;
            }

            case TerrariaMessageId.SignNew:
            {
                TerrariaSignDecodeResult decode = TerrariaSignCodec.TryDecodeState(
                    in frame,
                    out TerrariaSignState state);
                if (decode != TerrariaSignDecodeResult.Decoded)
                    return Stop(SignInteractionFrameStopReason.MalformedSignPacket);
                posted = ingress.TryPostUpdate(connection, in state);
                break;
            }

            default:
                return inner.OnFrame(in frame);
        }

        return posted
            ? TerrariaFrameSinkResult.Continue
            : Stop(SignInteractionFrameStopReason.GameIngressBackpressure);
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

    private TerrariaFrameSinkResult Stop(SignInteractionFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
