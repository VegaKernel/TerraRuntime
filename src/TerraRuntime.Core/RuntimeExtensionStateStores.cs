using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Fixed-capacity typed side state owned by one NPC behavior/extension. State is keyed by the runtime's
/// generation-safe handle and never by NPC content type or a reusable slot alone. Retiring an entity keeps the
/// generation tombstone so the same stale handle cannot resurrect state after despawn.
/// </summary>
public sealed class RuntimeNpcExtensionStateStore<TState>
{
    private readonly GenerationBoundStateSlot<TState>[] slots;

    public RuntimeNpcExtensionStateStore(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (capacity > byte.MaxValue + 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        slots = new GenerationBoundStateSlot<TState>[capacity];
    }

    public int Capacity => slots.Length;

    public bool TryActivate(NpcHandle handle) =>
        handle.IsAssigned && handle.Slot < slots.Length && slots[handle.Slot].TryActivate(handle.Generation.Value);

    public bool TryGet(NpcHandle handle, out TState state)
    {
        if (handle.IsAssigned && handle.Slot < slots.Length)
            return slots[handle.Slot].TryGet(handle.Generation.Value, out state);

        state = default!;
        return false;
    }

    public bool TrySet(NpcHandle handle, TState state) =>
        handle.IsAssigned && handle.Slot < slots.Length && slots[handle.Slot].TrySet(handle.Generation.Value, state);

    public bool TryRetire(NpcHandle handle) =>
        handle.IsAssigned && handle.Slot < slots.Length && slots[handle.Slot].TryRetire(handle.Generation.Value);

    public void ClearAll() => Array.Clear(slots);
}

/// <summary>
/// Fixed-capacity typed side state owned by one projectile behavior/extension. Projectile slot reuse cannot alias
/// state because every operation requires the exact runtime generation.
/// </summary>
public sealed class RuntimeProjectileExtensionStateStore<TState>
{
    private readonly GenerationBoundStateSlot<TState>[] slots;

    public RuntimeProjectileExtensionStateStore(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (capacity > ushort.MaxValue + 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        slots = new GenerationBoundStateSlot<TState>[capacity];
    }

    public int Capacity => slots.Length;

    public bool TryActivate(ProjectileHandle handle) =>
        handle.IsAssigned && handle.Slot < slots.Length && slots[handle.Slot].TryActivate(handle.Generation.Value);

    public bool TryGet(ProjectileHandle handle, out TState state)
    {
        if (handle.IsAssigned && handle.Slot < slots.Length)
            return slots[handle.Slot].TryGet(handle.Generation.Value, out state);

        state = default!;
        return false;
    }

    public bool TrySet(ProjectileHandle handle, TState state) =>
        handle.IsAssigned && handle.Slot < slots.Length && slots[handle.Slot].TrySet(handle.Generation.Value, state);

    public bool TryRetire(ProjectileHandle handle) =>
        handle.IsAssigned && handle.Slot < slots.Length && slots[handle.Slot].TryRetire(handle.Generation.Value);

    public void ClearAll() => Array.Clear(slots);
}

internal struct GenerationBoundStateSlot<TState>
{
    private ulong generation;
    private bool active;
    private TState state;

    public bool TryActivate(ulong nextGeneration)
    {
        if (nextGeneration == 0)
            return false;

        if (active)
        {
            if (generation == nextGeneration)
                return true;
            if (nextGeneration < generation)
                return false;
        }
        else if (nextGeneration <= generation)
        {
            return false;
        }

        generation = nextGeneration;
        active = true;
        state = default!;
        return true;
    }

    public bool TryGet(ulong expectedGeneration, out TState value)
    {
        if (active && generation == expectedGeneration)
        {
            value = state;
            return true;
        }

        value = default!;
        return false;
    }

    public bool TrySet(ulong expectedGeneration, TState value)
    {
        if (!active || generation != expectedGeneration)
            return false;

        state = value;
        return true;
    }

    public bool TryRetire(ulong expectedGeneration)
    {
        if (!active || generation != expectedGeneration)
            return false;

        active = false;
        state = default!;
        return true;
    }
}
