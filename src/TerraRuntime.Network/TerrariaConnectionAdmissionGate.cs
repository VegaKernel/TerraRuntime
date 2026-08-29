namespace TerraRuntime.Network;

public sealed class TerrariaConnectionAdmissionGate
{
    public const int DefaultMaxAdmissionsPerWindow = 512;
    public static TimeSpan DefaultAdmissionWindow { get; } = TimeSpan.FromSeconds(1);

    private readonly int _maxConnections;
    private readonly int _maxAdmissionsPerWindow;
    private readonly TimeSpan _admissionWindow;
    private readonly TimeProvider _timeProvider;
    private readonly object _admissionRateGate = new();
    private long _admissionWindowStarted;
    private int _admissionsInWindow;
    private int _activeConnections;
    private long _acceptedConnections;
    private long _rejectedConnections;
    private long _capacityRejectedConnections;
    private long _rateRejectedConnections;

    public TerrariaConnectionAdmissionGate(int maxConnections)
        : this(
            maxConnections,
            DefaultMaxAdmissionsPerWindow,
            DefaultAdmissionWindow,
            TimeProvider.System)
    {
    }

    public TerrariaConnectionAdmissionGate(
        int maxConnections,
        int maxAdmissionsPerWindow,
        TimeSpan admissionWindow,
        TimeProvider? timeProvider = null)
    {
        if (maxConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConnections));
        if (maxAdmissionsPerWindow <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAdmissionsPerWindow));
        if (admissionWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(admissionWindow));

        _maxConnections = maxConnections;
        _maxAdmissionsPerWindow = maxAdmissionsPerWindow;
        _admissionWindow = admissionWindow;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _admissionWindowStarted = _timeProvider.GetTimestamp();
    }

    public int MaxConnections => _maxConnections;

    public int MaxAdmissionsPerWindow => _maxAdmissionsPerWindow;

    public TimeSpan AdmissionWindow => _admissionWindow;

    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    public long AcceptedConnections => Interlocked.Read(ref _acceptedConnections);

    public long RejectedConnections => Interlocked.Read(ref _rejectedConnections);

    public long CapacityRejectedConnections => Interlocked.Read(ref _capacityRejectedConnections);

    public long RateRejectedConnections => Interlocked.Read(ref _rateRejectedConnections);

    public bool TryAcquire(out Lease? lease)
    {
        // Count every attempt before capacity admission. Otherwise a full server would permit an
        // unbounded accept/reject churn loop without ever consuming the rate budget.
        if (!TryConsumeAdmissionBudget())
        {
            RejectRate();
            lease = null;
            return false;
        }

        while (true)
        {
            int active = Volatile.Read(ref _activeConnections);
            if (active >= _maxConnections)
            {
                RejectCapacity();
                lease = null;
                return false;
            }

            if (Interlocked.CompareExchange(ref _activeConnections, active + 1, active) != active)
                continue;

            Interlocked.Increment(ref _acceptedConnections);
            lease = new Lease(this);
            return true;
        }
    }

    private bool TryConsumeAdmissionBudget()
    {
        lock (_admissionRateGate)
        {
            long now = _timeProvider.GetTimestamp();
            if (_timeProvider.GetElapsedTime(_admissionWindowStarted, now) >= _admissionWindow)
            {
                _admissionWindowStarted = now;
                _admissionsInWindow = 0;
            }

            if (_admissionsInWindow >= _maxAdmissionsPerWindow)
                return false;

            _admissionsInWindow++;
            return true;
        }
    }

    private void RejectCapacity()
    {
        Interlocked.Increment(ref _capacityRejectedConnections);
        Interlocked.Increment(ref _rejectedConnections);
    }

    private void RejectRate()
    {
        Interlocked.Increment(ref _rateRejectedConnections);
        Interlocked.Increment(ref _rejectedConnections);
    }

    private void Release()
    {
        int active = Interlocked.Decrement(ref _activeConnections);
        if (active < 0)
        {
            Interlocked.Increment(ref _activeConnections);
            throw new InvalidOperationException("Connection admission lease was released more than once.");
        }
    }

    public sealed class Lease : IDisposable
    {
        private TerrariaConnectionAdmissionGate? _owner;

        internal Lease(TerrariaConnectionAdmissionGate owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            TerrariaConnectionAdmissionGate? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
