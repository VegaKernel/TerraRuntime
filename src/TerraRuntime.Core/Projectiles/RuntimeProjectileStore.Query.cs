using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    public bool TryGetActive(ushort slot, out ProjectileSnapshot snapshot)
    {
        if (!IsAddressableSlot(slot))
        {
            snapshot = default;
            return false;
        }

        ref readonly SlotState state = ref _slots[slot];
        if (!state.Active)
        {
            snapshot = default;
            return false;
        }

        snapshot = Capture(slot, in state);
        return true;
    }

    public bool TryGet(ProjectileHandle handle, out ProjectileSnapshot snapshot)
    {
        if (!IsCurrentHandleCandidate(handle))
        {
            snapshot = default;
            return false;
        }

        ref readonly SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
        {
            snapshot = default;
            return false;
        }

        snapshot = Capture(handle.Slot, in state);
        return true;
    }

    public bool TryGetLifecycle(ProjectileHandle handle, out ProjectileLifecycleState lifecycle)
    {
        if (!IsCurrentHandleCandidate(handle))
        {
            lifecycle = default;
            return false;
        }

        ref readonly SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
        {
            lifecycle = default;
            return false;
        }

        lifecycle = state.Lifecycle;
        return true;
    }


    /// <summary>
    /// Returns whether this exact projectile generation came from a server-authoritative spawn path. Client packet-27
    /// generations remain untrusted until a source/damage validator explicitly promotes them.
    /// </summary>
    public bool IsCombatTrusted(ProjectileHandle handle)
    {
        if (!IsCurrentHandleCandidate(handle))
            return false;

        ref readonly SlotState state = ref _slots[handle.Slot];
        return state.Active && state.Generation == handle.Generation.Value && state.CombatTrusted;
    }

    /// <summary>
    /// Returns the exact player session that owns a combat-trusted player projectile generation. Server/NPC-owned
    /// trusted projectiles intentionally return false because they have no player-session provenance.
    /// </summary>
    public bool TryGetCombatTrustedOwner(ProjectileHandle handle, out PlayerHandle owner)
    {
        owner = default;
        if (!IsCurrentHandleCandidate(handle))
            return false;

        ref readonly SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value || !state.CombatTrusted ||
            !state.CombatTrustedOwner.IsAssigned || state.CombatTrustedOwner.Slot.Value != state.Update.Spawner)
        {
            return false;
        }

        owner = state.CombatTrustedOwner;
        return true;
    }


    /// <summary>Returns the generation-safe NPC source for a server-owned projectile spawned by authoritative NPC AI.</summary>
    public bool TryGetServerNpcSource(ProjectileHandle handle, out NpcHandle sourceNpc)
    {
        sourceNpc = default;
        if (!IsCurrentHandleCandidate(handle))
            return false;

        ref readonly SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value || !state.SourceNpc.IsAssigned ||
            state.Update.Spawner != byte.MaxValue)
        {
            return false;
        }

        sourceNpc = state.SourceNpc;
        return true;
    }

    public int CopyActive(Span<ProjectileSnapshot> destination)
    {
        if (destination.Length < _activeCount)
        {
            throw new ArgumentException(
                $"Destination length {destination.Length} is smaller than active projectile count {_activeCount}.",
                nameof(destination));
        }

        int written = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref readonly SlotState state = ref _slots[slot];
            if (!state.Active)
                continue;

            destination[written++] = Capture(checked((ushort)slot), in state);
        }

        return written;
    }
}
