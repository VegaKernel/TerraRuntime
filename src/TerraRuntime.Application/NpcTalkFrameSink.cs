using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum NpcTalkFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedTalkPacket = 2,
    InvalidNpcSlot = 3,
    GameIngressBackpressure = 4
}

/// <summary>
/// Connection-owned packet-40 ingress. The client-provided player byte is deliberately not trusted; authoritative
/// application rewrites it from the authenticated <see cref="ConnectionHandle"/> before replication.
/// </summary>
public sealed class NpcTalkFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly INpcTalkNetworkIngress ingress;

    internal NpcTalkFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        INpcTalkNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("NPC talk ingress requires a connection command source.", nameof(source));
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
        this.source = source;
    }

    public NpcTalkFrameStopReason StopReason { get; private set; }

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        NpcTalkFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        NpcTalkFrameStopReason.MalformedTalkPacket => TerrariaFrameRejectionCategory.MalformedProtocol,
        NpcTalkFrameStopReason.InvalidNpcSlot => TerrariaFrameRejectionCategory.InvalidState,
        NpcTalkFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => inner is ITerrariaFrameRejectionSource rejection
            ? rejection.RejectionCategory
            : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != NpcTalkFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.SetNpcTalk)
            return inner.OnFrame(in frame);

        if (bootstrap.JoinState != PlayerJoinState.Playing ||
            bootstrap.AssignedPlayerHandle is not PlayerHandle player)
        {
            return Stop(NpcTalkFrameStopReason.InvalidJoinState);
        }

        if (TerrariaNpcTalkCodec.TryDecode(in frame, out TerrariaNpcTalkState state) != TerrariaNpcTalkDecodeResult.Decoded)
            return Stop(NpcTalkFrameStopReason.MalformedTalkPacket);
        if (!TerrariaNpcTalkCodec.IsValidNpcSlot(state.NpcSlot))
            return Stop(NpcTalkFrameStopReason.InvalidNpcSlot);

        var connection = new ConnectionHandle(source, player);
        // Talk target is replaceable per-player UI state (NPC slot or -1). If the authoritative queue is
        // momentarily full, discard this stale sample rather than disconnecting a healthy client. A later talk/close
        // packet carries the current target; persistent NPC mutations use separate strict ingress paths.
        _ = ingress.TryPost(connection, in state);
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult Stop(NpcTalkFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
