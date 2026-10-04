using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum NpcBuffFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedPacket = 2,
    InvalidBuffState = 3,
}

/// <summary>Connection-owned packet-53 ingress; authoritative buff state is applied only by the game-loop owner.</summary>
public sealed class NpcBuffFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource, ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly INpcBuffNetworkIngress ingress;

    internal NpcBuffFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        INpcBuffNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("NPC buff ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public NpcBuffFrameStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == NpcBuffFrameStopReason.None && inner is ITerrariaConnectionStopReasonSource source
            ? source.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        NpcBuffFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        NpcBuffFrameStopReason.MalformedPacket => TerrariaFrameRejectionCategory.MalformedProtocol,
        NpcBuffFrameStopReason.InvalidBuffState => TerrariaFrameRejectionCategory.InvalidState,
        _ => inner is ITerrariaFrameRejectionSource rejection ? rejection.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != NpcBuffFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.AddNpcBuff)
            return inner.OnFrame(in frame);
        if (bootstrap.JoinState != PlayerJoinState.Playing || bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return Stop(NpcBuffFrameStopReason.InvalidJoinState);
        if (!TerrariaNpcBuffCodec.TryDecode(in frame, out TerrariaNpcBuffState state))
            return Stop(NpcBuffFrameStopReason.MalformedPacket);
        if (!TerrariaNpcBuffCodec.IsValid(in state))
            return Stop(NpcBuffFrameStopReason.InvalidBuffState);

        var connection = new ConnectionHandle(source, player);
        // AddBuff is a discrete authoritative proposal. Queue saturation rejects the action, not the connection.
        _ = ingress.TryPost(connection, in state);
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult Stop(NpcBuffFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
