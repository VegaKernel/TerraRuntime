using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Bounded allocator for Terraria player slots. Slots are leased independently from transport connection IDs.
/// </summary>
public sealed class PlayerSlotPool
{
    private readonly object _gate = new();
    private readonly bool[] _leased;
    private int _leasedCount;

    public PlayerSlotPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, byte.MaxValue);
        _leased = new bool[capacity];
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

                _leased[i] = true;
                _leasedCount++;
                lease = new PlayerSlotLease(this, new PlayerSlotId((byte)i));
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

        internal PlayerSlotLease(PlayerSlotPool owner, PlayerSlotId slot)
        {
            _owner = owner;
            Slot = slot;
        }

        public PlayerSlotId Slot { get; }

        public bool IsReleased => Volatile.Read(ref _owner) is null;

        public void Dispose()
        {
            PlayerSlotPool? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(Slot);
        }
    }
}
