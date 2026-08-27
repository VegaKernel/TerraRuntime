namespace TerraRuntime.Network;

/// <summary>
/// Single-writer per-connection frame/byte accounting with optional fixed-window enforcement.
/// The connection read path is the sole writer; metric readers may observe snapshots concurrently.
/// </summary>
public sealed class TerrariaConnectionRateAccountant
{
    private readonly ConnectionRateBudgetOptions _options;
    private readonly TimeProvider _timeProvider;
    private long _windowStartTimestamp;
    private int _windowFrames;
    private long _windowBytes;
    private long _totalFrames;
    private long _totalBytes;
    private long _rejectedFrames;

    public TerrariaConnectionRateAccountant(
        ConnectionRateBudgetOptions options,
        TimeProvider? timeProvider = null)
    {
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _windowStartTimestamp = _timeProvider.GetTimestamp();
    }

    public int CurrentWindowFrames => Volatile.Read(ref _windowFrames);

    public long CurrentWindowBytes => Volatile.Read(ref _windowBytes);

    public ConnectionRateSnapshot Snapshot => new(
        Interlocked.Read(ref _totalFrames),
        Interlocked.Read(ref _totalBytes),
        Interlocked.Read(ref _rejectedFrames));

    public ConnectionRateDecision Observe(int frameBytes)
    {
        if (frameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameBytes));
        }

        long now = _timeProvider.GetTimestamp();
        if (_timeProvider.GetElapsedTime(_windowStartTimestamp, now) >= _options.Window)
        {
            _windowStartTimestamp = now;
            Volatile.Write(ref _windowFrames, 0);
            Volatile.Write(ref _windowBytes, 0);
        }

        int windowFrames = _windowFrames + 1;
        long windowBytes = _windowBytes + frameBytes;
        Volatile.Write(ref _windowFrames, windowFrames);
        Volatile.Write(ref _windowBytes, windowBytes);
        Interlocked.Increment(ref _totalFrames);
        Interlocked.Add(ref _totalBytes, frameBytes);

        if (_options.MaxFrames is int maxFrames && windowFrames > maxFrames)
        {
            Interlocked.Increment(ref _rejectedFrames);
            return ConnectionRateDecision.FrameLimitExceeded;
        }

        if (_options.MaxBytes is long maxBytes && windowBytes > maxBytes)
        {
            Interlocked.Increment(ref _rejectedFrames);
            return ConnectionRateDecision.ByteLimitExceeded;
        }

        return ConnectionRateDecision.Allowed;
    }
}
