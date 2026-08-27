namespace TerraRuntime.Network;

public sealed class TerrariaConnectionAdmissionGate
{
    private readonly int _maxConnections;
    private int _activeConnections;
    private long _acceptedConnections;
    private long _rejectedConnections;

    public TerrariaConnectionAdmissionGate(int maxConnections)
    {
        if (maxConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConnections));
        }

        _maxConnections = maxConnections;
    }

    public int MaxConnections => _maxConnections;

    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    public long AcceptedConnections => Interlocked.Read(ref _acceptedConnections);

    public long RejectedConnections => Interlocked.Read(ref _rejectedConnections);

    public bool TryAcquire(out Lease? lease)
    {
        while (true)
        {
            int active = Volatile.Read(ref _activeConnections);
            if (active >= _maxConnections)
            {
                Interlocked.Increment(ref _rejectedConnections);
                lease = null;
                return false;
            }

            if (Interlocked.CompareExchange(ref _activeConnections, active + 1, active) == active)
            {
                Interlocked.Increment(ref _acceptedConnections);
                lease = new Lease(this);
                return true;
            }
        }
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
