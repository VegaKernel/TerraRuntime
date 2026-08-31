using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

public enum NpcHomeFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedHomeUpdate = 2,
    GameIngressBackpressure = 3
}

/// <summary>
/// Connection-owned packet-60 ingress. Wire decoding stays on the socket thread; room validation and mutation are
/// posted to the authoritative game loop. A decoded packet never directly changes NPC/world persistence state.
/// </summary>
public sealed class NpcHomeFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly ITownNpcHomeNetworkIngress ingress;

    internal NpcHomeFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        ITownNpcHomeNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Town-NPC home ingress requires a connection command source.", nameof(source));
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
        this.source = source;
    }

    public NpcHomeFrameStopReason StopReason { get; private set; }

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        NpcHomeFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        NpcHomeFrameStopReason.MalformedHomeUpdate => TerrariaFrameRejectionCategory.MalformedProtocol,
        NpcHomeFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => inner is ITerrariaFrameRejectionSource rejection
            ? rejection.RejectionCategory
            : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != NpcHomeFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.UpdateNpcHome)
            return inner.OnFrame(in frame);

        if (bootstrap.JoinState != PlayerJoinState.Playing ||
            bootstrap.AssignedPlayerHandle is not PlayerHandle player)
        {
            return Stop(NpcHomeFrameStopReason.InvalidJoinState);
        }

        if (TerrariaNpcHomeCodec.TryDecode(in frame, out TerrariaNpcHomeState state) != TerrariaNpcHomeDecodeResult.Decoded ||
            state.NpcSlot < 0 ||
            !state.TryGetStatus(out _))
        {
            return Stop(NpcHomeFrameStopReason.MalformedHomeUpdate);
        }

        var connection = new ConnectionHandle(source, player);
        return ingress.TryPost(connection, in state)
            ? TerrariaFrameSinkResult.Continue
            : Stop(NpcHomeFrameStopReason.GameIngressBackpressure);
    }

    private TerrariaFrameSinkResult Stop(NpcHomeFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
