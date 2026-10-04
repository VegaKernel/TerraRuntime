using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum PlayerLuckFactorsFrameStopReason : byte
{
    None = 0,
    MalformedLuckFactorsSnapshot = 1
}

/// <summary>
/// Connection-owned packet-134 ingress. The claimed player byte is discarded exactly like TerrariaServer 1.4.5.8
/// does in server mode. Finite factors feed the source luck calculation; absent world flags remain unadmitted.
/// </summary>
public sealed class PlayerLuckFactorsFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource, ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IPlayerLuckFactorsNetworkIngress ingress;

    internal PlayerLuckFactorsFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IPlayerLuckFactorsNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player factors ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public PlayerLuckFactorsFrameStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == PlayerLuckFactorsFrameStopReason.None && inner is ITerrariaConnectionStopReasonSource nested
            ? nested.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        PlayerLuckFactorsFrameStopReason.MalformedLuckFactorsSnapshot => TerrariaFrameRejectionCategory.MalformedProtocol,
        _ => inner is ITerrariaFrameRejectionSource nested ? nested.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerLuckFactorsFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.PlayerLuckFactors)
            return inner.OnFrame(in frame);

        if (bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return inner.OnFrame(in frame);

        TerrariaPlayerLuckFactorsDecodeResult decode = TerrariaPlayerLuckFactorsCodec1458.TryDecode(
            in frame,
            out _,
            out VanillaPlayerLuckComponents1458 factors);
        if (decode != TerrariaPlayerLuckFactorsDecodeResult.Decoded)
            return Stop(PlayerLuckFactorsFrameStopReason.MalformedLuckFactorsSnapshot);

        var connection = new ConnectionHandle(source, player);
        // Like appearance/equipment, this is a replaceable presentation snapshot. Queue pressure may drop one sample;
        // a later packet-134 update can replace this sample without turning load into a disconnect.
        _ = ingress.TryPost(connection, factors);
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult Stop(PlayerLuckFactorsFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
