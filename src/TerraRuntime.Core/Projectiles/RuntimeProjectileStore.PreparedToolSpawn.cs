using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal bool TryPrepareToolSpawn(in ProjectileStateUpdate update, out SpawnPreparation? preparation)
    {
        preparation = null;
        if (!IsValidState(in update) || !TryCreateLifecycle(update.Type, out var lifecycle) ||
            !TrySelectVanillaAllocationSlot(out ushort slot) ||
            _slots[slot].Generation == ulong.MaxValue ||
            _slots[slot].SourceDirection is not { } retainedDirection) return false;
        if (VanillaHeldToolProjectile1458.IsSupported(update.Type))
        {
            if (!VanillaHeldToolProjectile1458.TryFinalize(update.Type,
                update.PositionX, update.PositionY, update.VelocityX, update.VelocityY,
                retainedDirection, out var held) || lifecycle.TimeLeft != held.TimeLeft) return false;
            lifecycle = lifecycle with { HeldTool = held, PenetrateOverride = -1 };
        }
        else if (update.Type == new ProjectileTypeId(VanillaThrownAxeProjectile1458.ProjectileType))
        {
            if (!VanillaThrownAxeProjectile1458.TryFinalize(update.PositionX, update.PositionY,
                update.VelocityX, update.VelocityY, retainedDirection, out var axe) ||
                lifecycle.TimeLeft != axe.TimeLeft) return false;
            lifecycle = lifecycle with { ThrownAxe = axe, PenetrateOverride = -1 };
        }
        else return false;
        // Private held state is finalized with the selected physical slot, once
        // inside this qualified birth. Raw packet27 updates cannot initialize it.
        preparation = new(this, slot, in update, in lifecycle);
        return true;
    }

    internal bool TryGetSourceDirection(ushort slot, out int direction)
    {
        direction = 0;
        if (!IsAddressableSlot(slot) || _slots[slot].SourceDirection is not { } known) return false;
        direction = known;
        return true;
    }
}
