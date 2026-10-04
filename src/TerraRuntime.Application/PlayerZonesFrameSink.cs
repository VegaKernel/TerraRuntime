using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum PlayerZonesFrameStopReason : byte
{
    None = 0,
    MalformedZonesSnapshot = 1
}

/// <summary>
/// Connection-owned packet-36 ingress. The claimed player byte is discarded exactly like TerrariaServer 1.4.5.8
/// does in server mode. The seven-byte owned snapshot supplies source biome conversation topics; natural spawn trust is unchanged.
/// </summary>
public sealed class PlayerZonesFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource, ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IPlayerZonesNetworkIngress ingress;

    internal PlayerZonesFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IPlayerZonesNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player zones ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public PlayerZonesFrameStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == PlayerZonesFrameStopReason.None && inner is ITerrariaConnectionStopReasonSource nested
            ? nested.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        PlayerZonesFrameStopReason.MalformedZonesSnapshot => TerrariaFrameRejectionCategory.MalformedProtocol,
        _ => inner is ITerrariaFrameRejectionSource nested ? nested.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerZonesFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.SyncPlayerZone)
            return inner.OnFrame(in frame);

        if (bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return inner.OnFrame(in frame);

        TerrariaPlayerZonesDecodeResult decode = TerrariaPlayerZonesCodec1458.TryDecode(
            in frame,
            out _,
            out PlayerZoneSnapshot1458 zones);
        if (decode != TerrariaPlayerZonesDecodeResult.Decoded)
            return Stop(PlayerZonesFrameStopReason.MalformedZonesSnapshot);

        var connection = new ConnectionHandle(source, player);
        // Like appearance/equipment, this is a replaceable presentation snapshot. Queue pressure may drop one sample;
        // a later packet-36 update can replace this sample without turning load into a disconnect.
        _ = ingress.TryPost(connection, in zones);
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult Stop(PlayerZonesFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
