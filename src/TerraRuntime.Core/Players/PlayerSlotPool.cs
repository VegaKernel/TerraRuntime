using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public enum PlayerSlotLeaseKind : byte
{
    Connection = 1,
    ServerOwned = 2
}

/// <summary>
/// Bounded allocator for Terraria player slots. Connections and runtime-owned players draw from the same slot and
/// generation space, so one live lease is sufficient to prevent any other owner from claiming that wire identity.
/// </summary>
public sealed class PlayerSlotPool
{
    private readonly object _gate = new();
    private readonly bool[] _leased;
    private readonly PlayerSlotLeaseKind[] _leaseKinds;
    private readonly ulong[] _generations;
    private int _leasedCount;
    private int _connectionLeasedCount;
    private int _serverOwnedLeasedCount;

    public PlayerSlotPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, byte.MaxValue);
        _leased = new bool[capacity];
        _leaseKinds = new PlayerSlotLeaseKind[capacity];
        _generations = new ulong[capacity];
    }

    public int Capacity => _leased.Length;

    public int LeasedCount
    {
        get
        {
            lock (_gate)
            {
                return _leasedCount;
            }
        }
    }

    public int ConnectionLeasedCount
    {
        get
        {
            lock (_gate)
            {
                return _connectionLeasedCount;
            }
        }
    }

    public int ServerOwnedLeasedCount
    {
        get
        {
            lock (_gate)
            {
                return _serverOwnedLeasedCount;
            }
        }
    }

    /// <summary>
    /// Backward-compatible connection allocation used by the vanilla join path. Runtime-owned players must use
    /// <see cref="TryAcquireServerOwned"/> instead of pretending to be a transport connection.
    /// </summary>
    public bool TryAcquire(out PlayerSlotLease? lease) =>
        TryAcquireConnection(out lease);

    public bool TryAcquireConnection(out PlayerSlotLease? lease) =>
        TryAcquire(PlayerSlotLeaseKind.Connection, out lease);

    public bool TryAcquireServerOwned(out PlayerSlotLease? lease) =>
        TryAcquire(PlayerSlotLeaseKind.ServerOwned, out lease);

    private bool TryAcquire(PlayerSlotLeaseKind kind, out PlayerSlotLease? lease)
    {
        if (kind is not PlayerSlotLeaseKind.Connection and not PlayerSlotLeaseKind.ServerOwned)
            throw new ArgumentOutOfRangeException(nameof(kind));

        lock (_gate)
        {
            for (int i = 0; i < _leased.Length; i++)
            {
                if (_leased[i])
                    continue;

                // A generation must never wrap and collide with an ancient stale handle. Saturation
                // is unreachable in practice, but skipping the slot keeps the invariant explicit.
                if (_generations[i] == ulong.MaxValue)
                    continue;

                ulong generation = _generations[i] + 1;
                _leased[i] = true;
                _leaseKinds[i] = kind;
                _leasedCount++;
                if (kind == PlayerSlotLeaseKind.Connection)
                    _connectionLeasedCount++;
                else
                    _serverOwnedLeasedCount++;
                _generations[i] = generation;
                lease = new PlayerSlotLease(
                    this,
                    new PlayerHandle(
                        new PlayerSlotId((byte)i),
                        new PlayerSessionGeneration(generation)),
                    kind);
                return true;
            }
        }

        lease = null;
        return false;
    }

    private void Release(PlayerSlotId slot, PlayerSessionGeneration generation, PlayerSlotLeaseKind kind)
    {
        lock (_gate)
        {
            int index = slot.Value;
            if ((uint)index >= (uint)_leased.Length ||
                !_leased[index] ||
                _generations[index] != generation.Value ||
                _leaseKinds[index] != kind)
            {
                return;
            }

            _leased[index] = false;
            _leaseKinds[index] = default;
            _leasedCount--;
            if (kind == PlayerSlotLeaseKind.Connection)
                _connectionLeasedCount--;
            else
                _serverOwnedLeasedCount--;
        }
    }

    public sealed class PlayerSlotLease : IDisposable
    {
        private PlayerSlotPool? _owner;

        internal PlayerSlotLease(
            PlayerSlotPool owner,
            PlayerHandle handle,
            PlayerSlotLeaseKind kind)
        {
            _owner = owner;
            Handle = handle;
            Kind = kind;
        }

        public PlayerHandle Handle { get; }

        public PlayerSlotId Slot => Handle.Slot;

        public PlayerSessionGeneration Generation => Handle.Generation;

        public PlayerSlotLeaseKind Kind { get; }

        public bool IsReleased => Volatile.Read(ref _owner) is null;

        public void Dispose()
        {
            PlayerSlotPool? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(Slot, Generation, Kind);
        }
    }
}
