using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Bounded allocator for Terraria player slots. Slots are leased independently from transport connection IDs.
/// </summary>
public sealed class PlayerSlotPool
{
    private readonly object _gate = new();
    private readonly bool[] _leased;
    private readonly ulong[] _generations;
    private int _leasedCount;

    public PlayerSlotPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, byte.MaxValue);
        _leased = new bool[capacity];
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

    public bool TryAcquire(out PlayerSlotLease? lease)
    {
        lock (_gate)
        {
            for (int i = 0; i < _leased.Length; i++)
            {
                if (_leased[i])
                {
                    continue;
                }

                // A generation must never wrap and collide with an ancient stale handle. Saturation
                // is unreachable in practice, but skipping the slot keeps the invariant explicit.
                if (_generations[i] == ulong.MaxValue)
                    continue;

                ulong generation = _generations[i] + 1;
                _leased[i] = true;
                _leasedCount++;
                _generations[i] = generation;
                lease = new PlayerSlotLease(
                    this,
                    new PlayerHandle(
                        new PlayerSlotId((byte)i),
                        new PlayerSessionGeneration(generation)));
                return true;
            }
        }

        lease = null;
        return false;
    }

    private void Release(PlayerSlotId slot)
    {
        lock (_gate)
        {
            int index = slot.Value;
            if ((uint)index >= (uint)_leased.Length || !_leased[index])
            {
                return;
            }

            _leased[index] = false;
            _leasedCount--;
        }
    }

    public sealed class PlayerSlotLease : IDisposable
    {
        private PlayerSlotPool? _owner;

        internal PlayerSlotLease(PlayerSlotPool owner, PlayerHandle handle)
        {
            _owner = owner;
            Handle = handle;
        }

        public PlayerHandle Handle { get; }

        public PlayerSlotId Slot => Handle.Slot;

        public PlayerSessionGeneration Generation => Handle.Generation;

        public bool IsReleased => Volatile.Read(ref _owner) is null;

        public void Dispose()
        {
            PlayerSlotPool? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(Slot);
        }
    }
}
