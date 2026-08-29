namespace TerraRuntime.Network;

public sealed class TerrariaConnectionPolicyState
{
    private readonly object _gate = new();
    private readonly TerrariaConnectionPolicyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly long _connectedTimestamp;
    private long _lastInboundTimestamp;
    private bool _handshakeComplete;
    private TerrariaConnectionStopReason _stopReason;

    public TerrariaConnectionPolicyState(
        TerrariaConnectionPolicyOptions options,
        TimeProvider? timeProvider = null)
    {
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _connectedTimestamp = _timeProvider.GetTimestamp();
        _lastInboundTimestamp = _connectedTimestamp;
    }

    internal TerrariaConnectionPolicyOptions Options => _options;

    internal TimeProvider TimeProvider => _timeProvider;

    public bool HandshakeComplete
    {
        get
        {
            lock (_gate)
            {
                return _handshakeComplete;
            }
        }
    }

    public TerrariaConnectionStopReason StopReason
    {
        get
        {
            lock (_gate)
            {
                return _stopReason;
            }
        }
    }

    public void ObserveInbound()
    {
        lock (_gate)
        {
            if (_stopReason == TerrariaConnectionStopReason.None)
            {
                _lastInboundTimestamp = _timeProvider.GetTimestamp();
            }
        }
    }

    public bool TryCompleteHandshake()
    {
        lock (_gate)
        {
            if (_stopReason != TerrariaConnectionStopReason.None || _handshakeComplete)
            {
                return false;
            }

            _handshakeComplete = true;
            _lastInboundTimestamp = _timeProvider.GetTimestamp();
            return true;
        }
    }

    public bool TryStop(TerrariaConnectionStopReason reason)
    {
        if (reason == TerrariaConnectionStopReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        lock (_gate)
        {
            if (_stopReason != TerrariaConnectionStopReason.None)
            {
                return false;
            }

            _stopReason = reason;
            return true;
        }
    }

    public TimeSpan GetRemainingTimeout()
    {
        lock (_gate)
        {
            if (_stopReason != TerrariaConnectionStopReason.None)
            {
                return Timeout.InfiniteTimeSpan;
            }

            long origin = _handshakeComplete ? _lastInboundTimestamp : _connectedTimestamp;
            TimeSpan timeout = _handshakeComplete ? _options.IdleTimeout : _options.HandshakeTimeout;
            TimeSpan elapsed = _timeProvider.GetElapsedTime(origin, _timeProvider.GetTimestamp());
            TimeSpan remaining = timeout - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public bool TryExpire(out TerrariaConnectionStopReason reason)
    {
        lock (_gate)
        {
            if (_stopReason != TerrariaConnectionStopReason.None)
            {
                reason = _stopReason;
                return false;
            }

            long origin = _handshakeComplete ? _lastInboundTimestamp : _connectedTimestamp;
            TimeSpan timeout = _handshakeComplete ? _options.IdleTimeout : _options.HandshakeTimeout;
            if (_timeProvider.GetElapsedTime(origin, _timeProvider.GetTimestamp()) < timeout)
            {
                reason = TerrariaConnectionStopReason.None;
                return false;
            }

            reason = _handshakeComplete
                ? TerrariaConnectionStopReason.IdleTimeout
                : TerrariaConnectionStopReason.HandshakeTimeout;
            _stopReason = reason;
            return true;
        }
    }
}
