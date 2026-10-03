using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum PlayerStealthFrameStopReason : byte
{
    None = 0,
    MalformedStealthSnapshot = 1
}

/// <summary>
/// Connection-owned packet-84 ingress. The claimed player byte is discarded exactly like TerrariaServer 1.4.5.8
/// does in server mode. The finite bounded value is retained for source conversation visibility; no stealth combat modifier is inferred.
/// </summary>
public sealed class PlayerStealthFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource, ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IPlayerStealthNetworkIngress ingress;

    internal PlayerStealthFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IPlayerStealthNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player stealth ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public PlayerStealthFrameStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == PlayerStealthFrameStopReason.None && inner is ITerrariaConnectionStopReasonSource nested
            ? nested.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        PlayerStealthFrameStopReason.MalformedStealthSnapshot => TerrariaFrameRejectionCategory.MalformedProtocol,
        _ => inner is ITerrariaFrameRejectionSource nested ? nested.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerStealthFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.PlayerStealth)
            return inner.OnFrame(in frame);

        if (bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return inner.OnFrame(in frame);

        TerrariaPlayerStealthDecodeResult decode = TerrariaPlayerStealthCodec1458.TryDecode(
            in frame,
            out _,
            out float stealth);
        if (decode != TerrariaPlayerStealthDecodeResult.Decoded)
            return Stop(PlayerStealthFrameStopReason.MalformedStealthSnapshot);

        var connection = new ConnectionHandle(source, player);
        // Like appearance/equipment, this is a replaceable presentation snapshot. Queue pressure may drop one sample;
        // a later packet-84 update can replace this sample without turning load into a disconnect.
        _ = ingress.TryPost(connection, stealth);
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult Stop(PlayerStealthFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
