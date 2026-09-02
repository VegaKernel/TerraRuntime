using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

public enum NpcCatchFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedPacket = 2,
    InvalidNpcSlot = 3,
    GameIngressBackpressure = 4
}

/// <summary>Connection-owned packet-70 ingress; authoritative catch state is applied only by the game-loop owner.</summary>
public sealed class NpcCatchFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly INpcCatchNetworkIngress ingress;

    internal NpcCatchFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        INpcCatchNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("NPC catch ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public NpcCatchFrameStopReason StopReason { get; private set; }

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        NpcCatchFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        NpcCatchFrameStopReason.MalformedPacket => TerrariaFrameRejectionCategory.MalformedProtocol,
        NpcCatchFrameStopReason.InvalidNpcSlot => TerrariaFrameRejectionCategory.InvalidState,
        NpcCatchFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => inner is ITerrariaFrameRejectionSource rejection ? rejection.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != NpcCatchFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.CatchNpc)
            return inner.OnFrame(in frame);
        if (bootstrap.JoinState != PlayerJoinState.Playing || bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return Stop(NpcCatchFrameStopReason.InvalidJoinState);
        if (TerrariaNpcCatchCodec.TryDecode(in frame, out TerrariaNpcCatchState state) != TerrariaNpcCatchDecodeResult.Decoded)
            return Stop(NpcCatchFrameStopReason.MalformedPacket);
        if (!TerrariaNpcCatchCodec.IsValidNpcSlot(state.NpcSlot))
            return Stop(NpcCatchFrameStopReason.InvalidNpcSlot);

        var connection = new ConnectionHandle(source, player);
        return ingress.TryPost(connection, in state)
            ? TerrariaFrameSinkResult.Continue
            : Stop(NpcCatchFrameStopReason.GameIngressBackpressure);
    }

    private TerrariaFrameSinkResult Stop(NpcCatchFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
