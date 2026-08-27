using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

public sealed class TerrariaConnectionPolicySink : ITerrariaFrameSink
{
    private readonly ITerrariaFrameSink _inner;
    private readonly TerrariaConnectionPolicyState _state;

    public TerrariaConnectionPolicySink(
        ITerrariaFrameSink inner,
        TerrariaConnectionPolicyState state)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(state);
        _inner = inner;
        _state = state;
    }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        _state.ObserveInbound();

        if (!_state.HandshakeComplete)
        {
            if (frame.MessageId != (byte)TerrariaMessageId.Hello)
            {
                _state.TryStop(TerrariaConnectionStopReason.InvalidHandshake);
                return TerrariaFrameSinkResult.Stop;
            }

            ConnectRequestDecodeResult decodeResult = TerrariaConnectRequestDecoder.TryDecode(
                frame,
                out TerrariaConnectRequest request);

            if (decodeResult != ConnectRequestDecodeResult.Decoded)
            {
                _state.TryStop(TerrariaConnectionStopReason.InvalidHandshake);
                return TerrariaFrameSinkResult.Stop;
            }

            if (!request.IsCurrentProtocol)
            {
                _state.TryStop(TerrariaConnectionStopReason.UnsupportedProtocol);
                return TerrariaFrameSinkResult.Stop;
            }

            if (!_state.TryCompleteHandshake())
            {
                return TerrariaFrameSinkResult.Stop;
            }
        }
        else if (frame.MessageId == (byte)TerrariaMessageId.Hello)
        {
            _state.TryStop(TerrariaConnectionStopReason.InvalidHandshake);
            return TerrariaFrameSinkResult.Stop;
        }

        TerrariaFrameSinkResult result = _inner.OnFrame(in frame);
        if (result == TerrariaFrameSinkResult.Stop)
        {
            _state.TryStop(TerrariaConnectionStopReason.ApplicationStopped);
        }

        return result;
    }
}
