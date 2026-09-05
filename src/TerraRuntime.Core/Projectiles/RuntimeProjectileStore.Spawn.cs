using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    public bool TrySpawn(ushort slot, in ProjectileStateUpdate update, out ProjectileSnapshot snapshot)
    {
        if (!IsAddressableSlot(slot) ||
            !IsValidState(in update) ||
            !TryCreateLifecycle(update.Type, out ProjectileLifecycleState lifecycle))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (state.Active || !TryAdvance(ref state.Generation))
        {
            snapshot = default;
            return false;
        }

        InitializeSlot(ref state, in update, in lifecycle);
        _activeCount++;
        snapshot = Capture(slot, in state);
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in snapshot);
        return true;
    }

    /// <summary>
    /// Applies TerrariaServer 1.4.5.8 NewProjectileSetup slot selection. A full normal pool replaces the
    /// eligible projectile in place and emits only a Spawn commit for the new generation; vanilla does not
    /// Kill the displaced projectile or emit packet 29 before reusing that physical slot.
    /// </summary>
    public bool TrySpawnVanilla(in ProjectileStateUpdate update, out ProjectileSnapshot snapshot) =>
        TrySpawnVanilla(in update, timeLeftOverride: null, out snapshot);

    /// <summary>
    /// Applies NewProjectileSetup allocation while allowing a source-owned positive lifetime override to be
    /// committed with the spawn generation. This avoids inventing a second Update commit merely to reproduce
    /// NPC code that assigns projectile.timeLeft immediately after NewProjectile.
    /// </summary>
    public bool TrySpawnVanilla(
        in ProjectileStateUpdate update,
        int? timeLeftOverride,
        out ProjectileSnapshot snapshot)
    {
        if (timeLeftOverride is <= 0 ||
            !IsValidState(in update) ||
            !TryCreateLifecycle(update.Type, out ProjectileLifecycleState lifecycle))
        {
            snapshot = default;
            return false;
        }

        if (timeLeftOverride is int sourceTimeLeft)
            lifecycle = lifecycle with { TimeLeft = sourceTimeLeft };

        if (!TrySelectVanillaAllocationSlot(out ushort slot))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (!TryAdvance(ref state.Generation))
        {
            snapshot = default;
            return false;
        }

        bool wasActive = state.Active;
        InitializeSlot(ref state, in update, in lifecycle);
        if (!wasActive)
            _activeCount++;

        snapshot = Capture(slot, in state);
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in snapshot);
        return true;
    }

    private bool TrySelectVanillaAllocationSlot(out ushort slot)
    {
        int normalCapacity = Math.Min(_slots.Length, VanillaPhysicalSlotCount);
        for (int candidate = 0; candidate < normalCapacity; candidate++)
        {
            if (_slots[candidate].Active)
                continue;

            slot = checked((ushort)candidate);
            return true;
        }

        if (normalCapacity < VanillaPhysicalSlotCount)
        {
            slot = default;
            return false;
        }

        int selected = VanillaOverflowSlot;
        int lowestTimeLeft = VanillaOldestProjectileSentinelTimeLeft;
        for (int candidate = 0; candidate < VanillaPhysicalSlotCount; candidate++)
        {
            ref readonly SlotState state = ref _slots[candidate];
            if (state.Lifecycle.NetImportant || state.Lifecycle.TimeLeft >= lowestTimeLeft)
                continue;

            selected = candidate;
            lowestTimeLeft = state.Lifecycle.TimeLeft;
        }

        if (selected == VanillaOverflowSlot && _slots.Length <= VanillaOverflowSlot)
        {
            slot = default;
            return false;
        }

        slot = checked((ushort)selected);
        return true;
    }
}
