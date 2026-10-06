using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

public readonly record struct ProjectileNpcStatusAddition1458(BuffTypeId Type, int Duration);

public static class VanillaProjectileNpcStatus1458
{
    // Only intrinsic StatusNPC effects; equipment and projectile AI draws belong to their callers.
    public static bool TrySelect(ProjectileTypeId type, Func<int, int, int> next,
        out ProjectileNpcStatusAddition1458? addition)
    {
        ArgumentNullException.ThrowIfNull(next);
        addition = null;
        if (type == VanillaProjectileIds.FireArrow)
        {
            if (next(0, 3) == 0) addition = new(VanillaBuffIds.OnFire, 180);
        }
        else if (type == VanillaProjectileIds.Flamelash)
        {
            if (next(0, 2) == 0) addition = new(VanillaBuffIds.OnFire, next(240, 480));
        }
        else if (type == VanillaProjectileIds.PoisonedKnife)
        {
            if (next(0, 2) == 0) addition = new(VanillaBuffIds.Poisoned, 600);
        }
        else
        {
            return false;
        }
        return true;
    }
}
