using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

public sealed class TerrariaConnectionPolicySink : ITerrariaFrameSink
{
    private readonly ITerrariaFrameSink _inner;
    private readonly TerrariaConnectionPolicyState _state;
    private readonly TerrariaConnectionRateAccountant _rateAccountant;
    private readonly TerrariaMessageRateAccountant _messageRateAccountant;

    public TerrariaConnectionPolicySink(
        ITerrariaFrameSink inner,
        TerrariaConnectionPolicyState state)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(state);
        _inner = inner;
        _state = state;
        _rateAccountant = new TerrariaConnectionRateAccountant(
            state.Options.RateBudget,
            state.TimeProvider);
        _messageRateAccountant = new TerrariaMessageRateAccountant(
            state.Options.MessageRateLimits,
            state.TimeProvider);
    }

    public TerrariaConnectionPolicySink(
        ITerrariaFrameSink inner,
        TerrariaConnectionPolicyState state,
        TerrariaConnectionRateAccountant rateAccountant)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rateAccountant);
        _inner = inner;
        _state = state;
        _rateAccountant = rateAccountant;
        _messageRateAccountant = new TerrariaMessageRateAccountant(
            state.Options.MessageRateLimits,
            state.TimeProvider);
    }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        _state.ObserveInbound();

        if (_rateAccountant.Observe(frame.PacketLength) != ConnectionRateDecision.Allowed)
        {
            TerrariaFrameRejectionTelemetry.Record(TerrariaFrameRejectionCategory.RateLimited);
            _state.TryStop(TerrariaConnectionStopReason.RateLimited);
            return TerrariaFrameSinkResult.Stop;
        }

        if (_messageRateAccountant.Observe(frame.MessageId, frame.PacketLength) != ConnectionRateDecision.Allowed)
        {
            _rateAccountant.RecordSecondaryRateRejection();
            TerrariaFrameRejectionTelemetry.Record(TerrariaFrameRejectionCategory.RateLimited);
            _state.TryStop(TerrariaConnectionStopReason.RateLimited);
            return TerrariaFrameSinkResult.Stop;
        }

        bool completingHandshake = !_state.HandshakeComplete;
        if (completingHandshake)
        {
            if (frame.MessageId != (byte)TerrariaMessageId.Hello)
            {
                TerrariaFrameRejectionTelemetry.Record(TerrariaFrameRejectionCategory.InvalidState);
                _state.TryStop(TerrariaConnectionStopReason.InvalidHandshake);
                return TerrariaFrameSinkResult.Stop;
            }
        }
        else if (frame.MessageId == (byte)TerrariaMessageId.Hello)
        {
            TerrariaFrameRejectionTelemetry.Record(TerrariaFrameRejectionCategory.InvalidState);
            _state.TryStop(TerrariaConnectionStopReason.InvalidHandshake);
            return TerrariaFrameSinkResult.Stop;
        }

        TerrariaFrameSinkResult result = _inner.OnFrame(in frame);
        if (result == TerrariaFrameSinkResult.Stop)
        {
            TerrariaFrameRejectionCategory rejectionCategory =
                _inner is ITerrariaFrameRejectionSource rejectionSource
                    ? rejectionSource.RejectionCategory
                    : TerrariaFrameRejectionCategory.None;

            TerrariaFrameRejectionTelemetry.Record(rejectionCategory);

            TerrariaConnectionStopReason preciseStop =
                _inner is ITerrariaConnectionStopReasonSource stopReasonSource
                    ? stopReasonSource.ConnectionStopReason
                    : TerrariaConnectionStopReason.None;
            TerrariaConnectionStopReason stopReason = preciseStop != TerrariaConnectionStopReason.None
                ? preciseStop
                : rejectionCategory switch
                {
                    TerrariaFrameRejectionCategory.RateLimited => TerrariaConnectionStopReason.RateLimited,
                    TerrariaFrameRejectionCategory.None => TerrariaConnectionStopReason.ApplicationStopped,
                    _ => TerrariaConnectionStopReason.FrameRejected
                };
            _state.TryStop(stopReason);
            return result;
        }

        if (completingHandshake && !_state.TryCompleteHandshake())
            return TerrariaFrameSinkResult.Stop;

        return result;
    }
}
