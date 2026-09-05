using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

public enum PlayerVitalsStopReason : byte
{
    None = 0,
    MalformedHealth = 1,
    MalformedMana = 2,
    GameIngressBackpressure = 3
}

/// <summary>
/// Connection-owned player-vitals layer. The wire player id is deliberately discarded and replaced
/// with the exact generation-safe player handle assigned by the bootstrap session.
/// </summary>
public sealed class PlayerVitalsFrameSink :
    ITerrariaFrameSink,
    ITerrariaFrameRejectionSource,
    ITerrariaConnectionStopReasonSource
{
    private readonly GameCommandSourceId _source;
    private readonly PlayerBootstrapFrameSink _bootstrap;
    private readonly IPlayerHealthIngress _healthIngress;
    private readonly IPlayerManaIngress _manaIngress;
    private TerrariaConnectionStopReason _connectionStopReason;

    public PlayerVitalsFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        IPlayerHealthIngress healthIngress,
        IPlayerManaIngress manaIngress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player vitals ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(healthIngress);
        ArgumentNullException.ThrowIfNull(manaIngress);

        _source = source;
        _bootstrap = bootstrap;
        _healthIngress = healthIngress;
        _manaIngress = manaIngress;
    }

    public PlayerVitalsStopReason StopReason { get; private set; }

    public TerrariaConnectionStopReason ConnectionStopReason => _connectionStopReason;

    public TerrariaFrameRejectionCategory RejectionCategory
    {
        get
        {
            if (_connectionStopReason == TerrariaConnectionStopReason.UnsupportedProtocol)
                return TerrariaFrameRejectionCategory.None;

            return StopReason switch
            {
                PlayerVitalsStopReason.MalformedHealth or PlayerVitalsStopReason.MalformedMana => TerrariaFrameRejectionCategory.MalformedProtocol,
                PlayerVitalsStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
                _ => ClassifyBootstrapRejection(_bootstrap.StopReason)
            };
        }
    }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerVitalsStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        return (TerrariaMessageId)frame.MessageId switch
        {
            TerrariaMessageId.PlayerHp => HandleHealth(frame),
            TerrariaMessageId.PlayerMana => HandleMana(frame),
            _ => DelegateToBootstrap(in frame)
        };
    }

    private TerrariaFrameSinkResult HandleHealth(in TerrariaFrame frame)
    {
        if (_bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return DelegateToBootstrap(in frame);

        TerrariaPlayerHealthDecodeResult decode = TerrariaPlayerVitalsCodec.TryDecodeHealth(
            frame,
            out TerrariaPlayerHealthState health);
        if (decode != TerrariaPlayerHealthDecodeResult.Decoded)
            return Stop(PlayerVitalsStopReason.MalformedHealth);

        var connection = new ConnectionHandle(_source, player);
        var request = new PlayerHealthCommitRequest(player.Slot, health.Life, health.MaxLife);
        return _healthIngress.TryPost(connection, in request)
            ? TerrariaFrameSinkResult.Continue
            : Stop(PlayerVitalsStopReason.GameIngressBackpressure);
    }

    private TerrariaFrameSinkResult HandleMana(in TerrariaFrame frame)
    {
        if (_bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return DelegateToBootstrap(in frame);

        TerrariaPlayerManaDecodeResult decode = TerrariaPlayerVitalsCodec.TryDecodeMana(
            frame,
            out TerrariaPlayerManaState mana);
        if (decode != TerrariaPlayerManaDecodeResult.Decoded)
            return Stop(PlayerVitalsStopReason.MalformedMana);

        var connection = new ConnectionHandle(_source, player);
        var request = new PlayerManaCommitRequest(player.Slot, mana.Mana, mana.MaxMana);
        return _manaIngress.TryPost(connection, in request)
            ? TerrariaFrameSinkResult.Continue
            : Stop(PlayerVitalsStopReason.GameIngressBackpressure);
    }

    private TerrariaFrameSinkResult DelegateToBootstrap(in TerrariaFrame frame)
    {
        bool initialHello =
            _bootstrap.AssignedPlayerHandle is null &&
            frame.MessageId == (byte)TerrariaMessageId.Hello;
        if (initialHello &&
            TerrariaConnectRequestDecoder.TryDecode(frame, out TerrariaConnectRequest request) == ConnectRequestDecodeResult.Decoded &&
            !request.IsCurrentProtocol)
        {
            _connectionStopReason = TerrariaConnectionStopReason.UnsupportedProtocol;
        }

        TerrariaFrameSinkResult result = _bootstrap.OnFrame(in frame);
        if (result != TerrariaFrameSinkResult.Stop)
            _connectionStopReason = TerrariaConnectionStopReason.None;
        return result;
    }

    private static TerrariaFrameRejectionCategory ClassifyBootstrapRejection(PlayerBootstrapStopReason reason) => reason switch
    {
        PlayerBootstrapStopReason.InvalidHandshake or
        PlayerBootstrapStopReason.MalformedJoinRequest or
        PlayerBootstrapStopReason.MalformedPlayerMovement or
        PlayerBootstrapStopReason.MalformedPlayerAppearance or
        PlayerBootstrapStopReason.MalformedPlayerEquipment or
        PlayerBootstrapStopReason.MalformedPlayerSpawn or
        PlayerBootstrapStopReason.MalformedChat => TerrariaFrameRejectionCategory.MalformedProtocol,
        PlayerBootstrapStopReason.InvalidJoinState or
        PlayerBootstrapStopReason.PlayerSlotMismatch => TerrariaFrameRejectionCategory.InvalidState,
        PlayerBootstrapStopReason.SectionWorkRateLimited => TerrariaFrameRejectionCategory.RateLimited,
        PlayerBootstrapStopReason.OutboundBackpressure or
        PlayerBootstrapStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => TerrariaFrameRejectionCategory.None
    };

    private TerrariaFrameSinkResult Stop(PlayerVitalsStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
