namespace TerraRuntime.Network;

public sealed class TerrariaConnectionPolicyState
{
    private readonly object _gate = new();
    private readonly TerrariaConnectionPolicyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly long _connectedTimestamp;
    private readonly TaskCompletionSource<bool> _handshakeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _handshakeCompletedTimestamp;
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

    internal Task HandshakeSignal => _handshakeSignal.Task;

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
                _lastInboundTimestamp = _timeProvider.GetTimestamp();
        }
    }

    public bool TryCompleteHandshake()
    {
        lock (_gate)
        {
            if (_stopReason != TerrariaConnectionStopReason.None || _handshakeComplete)
                return false;

            long now = _timeProvider.GetTimestamp();
            _handshakeComplete = true;
            _handshakeCompletedTimestamp = now;
            _lastInboundTimestamp = now;
        }

        _handshakeSignal.TrySetResult(true);
        return true;
    }

    public bool TryStop(TerrariaConnectionStopReason reason)
    {
        if (reason == TerrariaConnectionStopReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason));

        lock (_gate)
        {
            if (_stopReason != TerrariaConnectionStopReason.None)
                return false;

            _stopReason = reason;
            return true;
        }
    }

    public TimeSpan GetRemainingTimeout() => GetRemainingTimeout(connectionReady: true);

    public TimeSpan GetRemainingTimeout(bool connectionReady)
    {
        lock (_gate)
        {
            if (_stopReason != TerrariaConnectionStopReason.None)
                return Timeout.InfiniteTimeSpan;

            if (!_handshakeComplete)
            {
                return GetRemaining(
                    _connectedTimestamp,
                    _options.HandshakeTimeout);
            }

            if (!connectionReady)
            {
                if (_options.JoinTimeout == Timeout.InfiniteTimeSpan)
                    return Timeout.InfiniteTimeSpan;

                return GetRemaining(
                    _handshakeCompletedTimestamp,
                    _options.JoinTimeout);
            }

            if (_options.IdleTimeout == Timeout.InfiniteTimeSpan)
                return Timeout.InfiniteTimeSpan;

            return GetRemaining(
                _lastInboundTimestamp,
                _options.IdleTimeout);
        }
    }

    public bool TryExpire(out TerrariaConnectionStopReason reason) =>
        TryExpire(connectionReady: true, out reason);

    public bool TryExpire(bool connectionReady, out TerrariaConnectionStopReason reason)
    {
        lock (_gate)
        {
            if (_stopReason != TerrariaConnectionStopReason.None)
            {
                reason = _stopReason;
                return false;
            }

            long origin;
            TimeSpan timeout;
            TerrariaConnectionStopReason expirationReason;

            if (!_handshakeComplete)
            {
                origin = _connectedTimestamp;
                timeout = _options.HandshakeTimeout;
                expirationReason = TerrariaConnectionStopReason.HandshakeTimeout;
            }
            else if (!connectionReady)
            {
                if (_options.JoinTimeout == Timeout.InfiniteTimeSpan)
                {
                    reason = TerrariaConnectionStopReason.None;
                    return false;
                }

                origin = _handshakeCompletedTimestamp;
                timeout = _options.JoinTimeout;
                expirationReason = TerrariaConnectionStopReason.JoinTimeout;
            }
            else
            {
                if (_options.IdleTimeout == Timeout.InfiniteTimeSpan)
                {
                    reason = TerrariaConnectionStopReason.None;
                    return false;
                }

                origin = _lastInboundTimestamp;
                timeout = _options.IdleTimeout;
                expirationReason = TerrariaConnectionStopReason.IdleTimeout;
            }

            if (_timeProvider.GetElapsedTime(origin, _timeProvider.GetTimestamp()) < timeout)
            {
                reason = TerrariaConnectionStopReason.None;
                return false;
            }

            reason = expirationReason;
            _stopReason = reason;
            return true;
        }
    }

    private TimeSpan GetRemaining(long origin, TimeSpan timeout)
    {
        TimeSpan elapsed = _timeProvider.GetElapsedTime(origin, _timeProvider.GetTimestamp());
        TimeSpan remaining = timeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
