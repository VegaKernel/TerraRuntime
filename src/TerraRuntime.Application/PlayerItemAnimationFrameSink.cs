using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum PlayerItemAnimationFrameStopReason : byte
{
    None = 0,
    MalformedItemAnimationSnapshot = 1
}

/// <summary>
/// Connection-owned packet-41 ingress. The claimed player byte is discarded exactly like TerrariaServer 1.4.5.8
/// does in server mode. The finite bounded value is retained for NPC visibility; no attack or damage authorization is inferred.
/// </summary>
public sealed class PlayerItemAnimationFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource, ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IPlayerItemAnimationNetworkIngress ingress;

    internal PlayerItemAnimationFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IPlayerItemAnimationNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player item-animation ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public PlayerItemAnimationFrameStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == PlayerItemAnimationFrameStopReason.None && inner is ITerrariaConnectionStopReasonSource nested
            ? nested.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        PlayerItemAnimationFrameStopReason.MalformedItemAnimationSnapshot => TerrariaFrameRejectionCategory.MalformedProtocol,
        _ => inner is ITerrariaFrameRejectionSource nested ? nested.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerItemAnimationFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.PlayerItemAnimation)
            return inner.OnFrame(in frame);

        if (bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return inner.OnFrame(in frame);

        TerrariaPlayerItemAnimationDecodeResult decode = TerrariaPlayerItemAnimationCodec1458.TryDecode(
            in frame,
            out _,
            out float rotation, out short animation);
        if (decode != TerrariaPlayerItemAnimationDecodeResult.Decoded)
            return Stop(PlayerItemAnimationFrameStopReason.MalformedItemAnimationSnapshot);

        var connection = new ConnectionHandle(source, player);
        // Like appearance/equipment, this is a replaceable presentation snapshot. Queue pressure may drop one sample;
        // a later packet-41 change converges state without turning load into a disconnect.
        _ = ingress.TryPost(connection, rotation, animation);
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult Stop(PlayerItemAnimationFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
