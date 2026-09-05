using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum PlayerCombatFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedPvpToggle = 2,
    MalformedTeam = 3,
    MalformedHurt = 4,
    GameIngressBackpressure = 5
}

/// <summary>
/// Connection-owned packet 30/45/117 ingress. Claimed source player ids on 30/45 are discarded exactly like the
/// vanilla dedicated server's whoAmI override. Packet 117 is only a combat claim; damage/item/crit fields are queued
/// for authoritative validation and never mutate player HP on the socket thread.
/// </summary>
public sealed class PlayerCombatFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource, ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IPlayerCombatNetworkIngress ingress;

    internal PlayerCombatFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IPlayerCombatNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player combat ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public PlayerCombatFrameStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason =>
        StopReason == PlayerCombatFrameStopReason.None && inner is ITerrariaConnectionStopReasonSource nested
            ? nested.ConnectionStopReason
            : TerrariaConnectionStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        PlayerCombatFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        PlayerCombatFrameStopReason.MalformedPvpToggle or PlayerCombatFrameStopReason.MalformedTeam or PlayerCombatFrameStopReason.MalformedHurt => TerrariaFrameRejectionCategory.MalformedProtocol,
        PlayerCombatFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => inner is ITerrariaFrameRejectionSource nested ? nested.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerCombatFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        return (TerrariaMessageId)frame.MessageId switch
        {
            TerrariaMessageId.TogglePvp => HandlePvpToggle(in frame),
            TerrariaMessageId.PlayerTeam => HandleTeam(in frame),
            TerrariaMessageId.PlayerHurt => HandleHurt(in frame),
            _ => inner.OnFrame(in frame)
        };
    }

    private TerrariaFrameSinkResult HandlePvpToggle(in TerrariaFrame frame)
    {
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(PlayerCombatFrameStopReason.InvalidJoinState);
        if (!TerrariaPlayerCombatCodec.TryDecodePvpToggle(in frame, out _, out bool hostile))
            return Stop(PlayerCombatFrameStopReason.MalformedPvpToggle);
        return ingress.TryPostPvpToggle(connection, hostile)
            ? TerrariaFrameSinkResult.Continue
            : Stop(PlayerCombatFrameStopReason.GameIngressBackpressure);
    }

    private TerrariaFrameSinkResult HandleTeam(in TerrariaFrame frame)
    {
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(PlayerCombatFrameStopReason.InvalidJoinState);
        if (!TerrariaPlayerCombatCodec.TryDecodeTeam(in frame, out _, out byte team))
            return Stop(PlayerCombatFrameStopReason.MalformedTeam);
        return ingress.TryPostTeam(connection, team)
            ? TerrariaFrameSinkResult.Continue
            : Stop(PlayerCombatFrameStopReason.GameIngressBackpressure);
    }

    private TerrariaFrameSinkResult HandleHurt(in TerrariaFrame frame)
    {
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(PlayerCombatFrameStopReason.InvalidJoinState);
        if (TerrariaPlayerCombatCodec.TryDecodeHurt(in frame, out TerrariaPlayerHurtState state) != TerrariaPlayerHurtDecodeResult.Decoded)
            return Stop(PlayerCombatFrameStopReason.MalformedHurt);
        if (!state.Pvp)
            return inner.OnFrame(in frame);
        return ingress.TryPostPvpHit(connection, in state)
            ? TerrariaFrameSinkResult.Continue
            : Stop(PlayerCombatFrameStopReason.GameIngressBackpressure);
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

    private TerrariaFrameSinkResult Stop(PlayerCombatFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
