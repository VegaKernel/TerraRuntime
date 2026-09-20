using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum PlayerDoorToggleFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedDoorToggle = 2
}

/// <summary>
/// Connection-owned packet-19 ingress for the verified normal-door actions. Trapdoor and tall-gate source actions
/// continue through the chain until their distinct authorities are implemented.
/// </summary>
public sealed class PlayerDoorToggleFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IPlayerDoorToggleNetworkIngress ingress;

    internal PlayerDoorToggleFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IPlayerDoorToggleNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player-door ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ingress);
        this.source = source;
        this.bootstrap = bootstrap;
        this.inner = inner;
        this.ingress = ingress;
    }

    public PlayerDoorToggleFrameStopReason StopReason { get; private set; }

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        PlayerDoorToggleFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        PlayerDoorToggleFrameStopReason.MalformedDoorToggle => TerrariaFrameRejectionCategory.MalformedProtocol,
        _ => inner is ITerrariaFrameRejectionSource source
            ? source.RejectionCategory
            : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerDoorToggleFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.DoorToggle)
            return inner.OnFrame(in frame);

        TerrariaDoorToggleDecodeResult decode = TerrariaDoorToggleCodec.TryDecode(in frame, out TerrariaDoorToggleState state);
        if (decode != TerrariaDoorToggleDecodeResult.Decoded)
            return Stop(PlayerDoorToggleFrameStopReason.MalformedDoorToggle);
        if (state.Action is not (byte)TerrariaDoorToggleAction.OpenDoor and not (byte)TerrariaDoorToggleAction.CloseDoor)
            return inner.OnFrame(in frame);
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(PlayerDoorToggleFrameStopReason.InvalidJoinState);

        if (state.Action == (byte)TerrariaDoorToggleAction.OpenDoor)
            _ = ingress.TryPostDoorOpen(connection, in state);
        else
            _ = ingress.TryPostDoorClose(connection, in state);
        return TerrariaFrameSinkResult.Continue;
    }

    private bool TryGetPlayingConnection(out ConnectionHandle connection)
    {
        if (bootstrap.JoinState == PlayerJoinState.Playing && bootstrap.AssignedPlayerHandle is PlayerHandle player)
        {
            connection = new ConnectionHandle(source, player);
            return true;
        }

        connection = default;
        return false;
    }

    private TerrariaFrameSinkResult Stop(PlayerDoorToggleFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
