using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

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
public sealed class PlayerVitalsFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId _source;
    private readonly PlayerBootstrapFrameSink _bootstrap;
    private readonly IPlayerHealthIngress _healthIngress;
    private readonly IPlayerManaIngress _manaIngress;

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

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        PlayerVitalsStopReason.MalformedHealth or PlayerVitalsStopReason.MalformedMana => TerrariaFrameRejectionCategory.MalformedProtocol,
        PlayerVitalsStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => _bootstrap.RejectionCategory
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerVitalsStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        return (TerrariaMessageId)frame.MessageId switch
        {
            TerrariaMessageId.PlayerHp => HandleHealth(frame),
            TerrariaMessageId.PlayerMana => HandleMana(frame),
            _ => _bootstrap.OnFrame(in frame)
        };
    }

    private TerrariaFrameSinkResult HandleHealth(in TerrariaFrame frame)
    {
        if (_bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return _bootstrap.OnFrame(in frame);

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
            return _bootstrap.OnFrame(in frame);

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

    private TerrariaFrameSinkResult Stop(PlayerVitalsStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
