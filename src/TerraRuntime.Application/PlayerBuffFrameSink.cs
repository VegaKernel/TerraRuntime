using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum PlayerBuffFrameStopReason : byte
{
    None = 0,
    MalformedBuffSnapshot = 1
}

/// <summary>
/// Connection-owned packet-50 ingress. The claimed player byte is discarded exactly like TerrariaServer 1.4.5.8
/// does in server mode. The resulting type list is presentation state only; packet 50 carries no trustworthy time.
/// </summary>
public sealed class PlayerBuffFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource, ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IPlayerBuffNetworkIngress ingress;

    internal PlayerBuffFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IPlayerBuffNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player buff ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public PlayerBuffFrameStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == PlayerBuffFrameStopReason.None && inner is ITerrariaConnectionStopReasonSource nested
            ? nested.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        PlayerBuffFrameStopReason.MalformedBuffSnapshot => TerrariaFrameRejectionCategory.MalformedProtocol,
        _ => inner is ITerrariaFrameRejectionSource nested ? nested.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerBuffFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.PlayerBuffs)
            return inner.OnFrame(in frame);

        if (bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return inner.OnFrame(in frame);

        TerrariaPlayerBuffDecodeResult decode = TerrariaPlayerBuffCodec1458.TryDecode(
            in frame,
            out _,
            out BuffTypeId[] buffTypes);
        if (decode != TerrariaPlayerBuffDecodeResult.Decoded)
            return Stop(PlayerBuffFrameStopReason.MalformedBuffSnapshot);

        var connection = new ConnectionHandle(source, player);
        // Like appearance/equipment, this is a replaceable presentation snapshot. Queue pressure may drop one sample;
        // a later packet-50 change or reconnect baseline converges state without turning load into a disconnect.
        _ = ingress.TryPost(connection, buffTypes);
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult Stop(PlayerBuffFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
