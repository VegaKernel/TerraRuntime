using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    // Player.CheckCanUse/CheckItemUse (1458) scans physical0..999 for the
    // selected owner's type1052. Overflow1000 and unrelated actors do not
    // participate; client-reported ownedProjectileCounts is not a certificate.
    internal bool TryPrepareSourceOwnerTypeQuery(byte spawner, ProjectileTypeId type,
        out SourceOwnerTypeQueryPreparation? preparation)
    {
        preparation = null;
        if (spawner == byte.MaxValue || type == default) return false;
        preparation = new(this, spawner, type, HasSourceOwnerType(spawner, type));
        return true;
    }

    private bool HasSourceOwnerType(byte spawner, ProjectileTypeId type)
    {
        for (int slot = 0; slot < Math.Min(_slots.Length, VanillaPhysicalSlotCount); slot++)
        {
            ref readonly var state = ref _slots[slot];
            if (state.Active && state.Update.Spawner == spawner && state.Update.Type == type) return true;
        }
        return false;
    }

    internal sealed class SourceOwnerTypeQueryPreparation(
        RuntimeProjectileStore owner, byte spawner, ProjectileTypeId type, bool contains)
    {
        // CheckCanUse reads existence, not the selected actor's geometry or
        // identity. Keep a blocked use represented too, and invalidate either
        // polarity when the complete source membership predicate changes.
        // No retained mirror, broad whole-store revision or history is needed.
        internal bool Contains => contains;
        internal bool IsCurrentOwned => owner.HasSourceOwnerType(spawner, type) == contains;
    }
}
