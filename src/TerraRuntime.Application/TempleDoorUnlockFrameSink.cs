using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum TempleDoorUnlockFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedLockAndUnlock = 2
}

/// <summary>
/// Connection-owned packet-52 ingress for the Temple Key door action. Only action 2 has an implemented owner;
/// actions 1 and 3 continue to the surrounding packet chain until chest lock authority is added.
/// </summary>
public sealed class TempleDoorUnlockFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly ITempleDoorUnlockNetworkIngress ingress;

    internal TempleDoorUnlockFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        ITempleDoorUnlockNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Temple-door ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ingress);

        this.source = source;
        this.bootstrap = bootstrap;
        this.inner = inner;
        this.ingress = ingress;
    }

    public TempleDoorUnlockFrameStopReason StopReason { get; private set; }

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        TempleDoorUnlockFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        TempleDoorUnlockFrameStopReason.MalformedLockAndUnlock => TerrariaFrameRejectionCategory.MalformedProtocol,
        _ => inner is ITerrariaFrameRejectionSource source
            ? source.RejectionCategory
            : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != TempleDoorUnlockFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.LockAndUnlock)
            return inner.OnFrame(in frame);

        TerrariaLockAndUnlockDecodeResult decode = TerrariaLockAndUnlockCodec.TryDecode(in frame, out TerrariaLockAndUnlockState state);
        if (decode != TerrariaLockAndUnlockDecodeResult.Decoded)
            return Stop(TempleDoorUnlockFrameStopReason.MalformedLockAndUnlock);
        if (state.Action != 2)
            return inner.OnFrame(in frame);
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(TempleDoorUnlockFrameStopReason.InvalidJoinState);

        _ = ingress.TryPostTempleDoorUnlock(connection, in state);
        return TerrariaFrameSinkResult.Continue;
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

    private TerrariaFrameSinkResult Stop(TempleDoorUnlockFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
