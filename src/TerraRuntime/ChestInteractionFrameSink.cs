using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

public enum ChestInteractionFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedChestPacket = 2,
    GameIngressBackpressure = 3
}

/// <summary>
/// Connection-owned protocol-326 chest boundary for packets 31, 32, 33 and 69. The socket thread decodes only;
/// every world-chest decision is posted with the exact playing-session identity to the authoritative game loop.
/// </summary>
public sealed class ChestInteractionFrameSink : ITerrariaFrameSink
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IChestNetworkIngress ingress;

    internal ChestInteractionFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IChestNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Chest ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ingress);

        this.source = source;
        this.bootstrap = bootstrap;
        this.inner = inner;
        this.ingress = ingress;
    }

    public ChestInteractionFrameStopReason StopReason { get; private set; }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != ChestInteractionFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        TerrariaMessageId messageId = (TerrariaMessageId)frame.MessageId;
        if (messageId is not TerrariaMessageId.RequestChestOpen and
            not TerrariaMessageId.SyncChestItem and
            not TerrariaMessageId.SyncPlayerChest and
            not TerrariaMessageId.ChestName)
        {
            return inner.OnFrame(in frame);
        }

        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(ChestInteractionFrameStopReason.InvalidJoinState);

        bool posted;
        switch (messageId)
        {
            case TerrariaMessageId.RequestChestOpen:
            {
                TerrariaChestDecodeResult decode = TerrariaChestCodec.TryDecodeOpenRequest(in frame, out TerrariaChestOpenRequest request);
                if (decode != TerrariaChestDecodeResult.Decoded)
                    return Stop(ChestInteractionFrameStopReason.MalformedChestPacket);
                posted = ingress.TryPostOpen(connection, in request);
                break;
            }

            case TerrariaMessageId.SyncChestItem:
            {
                TerrariaChestDecodeResult decode = TerrariaChestCodec.TryDecodeItem(in frame, out TerrariaChestItemState state);
                if (decode != TerrariaChestDecodeResult.Decoded)
                    return Stop(ChestInteractionFrameStopReason.MalformedChestPacket);
                posted = ingress.TryPostItem(connection, in state);
                break;
            }

            case TerrariaMessageId.SyncPlayerChest:
            {
                TerrariaChestDecodeResult decode = TerrariaChestCodec.TryDecodeActiveChest(in frame, out TerrariaActiveChestState state);
                if (decode != TerrariaChestDecodeResult.Decoded)
                    return Stop(ChestInteractionFrameStopReason.MalformedChestPacket);
                posted = ingress.TryPostActiveState(connection, in state);
                break;
            }

            case TerrariaMessageId.ChestName:
            {
                TerrariaChestDecodeResult decode = TerrariaChestCodec.TryDecodeNameLookup(in frame, out TerrariaChestNameLookupRequest request);
                if (decode != TerrariaChestDecodeResult.Decoded)
                    return Stop(ChestInteractionFrameStopReason.MalformedChestPacket);
                posted = ingress.TryPostNameLookup(connection, in request);
                break;
            }

            default:
                return inner.OnFrame(in frame);
        }

        return posted
            ? TerrariaFrameSinkResult.Continue
            : Stop(ChestInteractionFrameStopReason.GameIngressBackpressure);
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

    private TerrariaFrameSinkResult Stop(ChestInteractionFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
