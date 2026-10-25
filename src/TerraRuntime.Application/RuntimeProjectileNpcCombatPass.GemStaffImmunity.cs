using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileNpcCombatPass
{
    internal bool IsGemStaffLocallyImmune(ProjectileHandle projectile, NpcHandle target, long sourceUpdateCount) =>
        localNpcImmunity.IsImmune(projectile, target, sourceUpdateCount,
            VanillaGemStaffProjectile1458.LocalNpcHitCooldown);

    internal bool TryResetGemStaffKillImmunity(in ProjectileSnapshot expected)
    {
        if (!projectiles.TryGet(expected.Handle, out var current) || current != expected ||
            !projectiles.TryGetLifecycle(expected.Handle, out var lifecycle) ||
            !lifecycle.GemStaffKillInProgress || lifecycle.GemStaff is null ||
            lifecycle.GemStaffFeatures is not { } features ||
            (features & GemStaffFeatures1458.AoeExplosion) == 0 ||
            !projectiles.TryGetCombatTrustedOwner(expected.Handle, out _)) return false;
        localNpcImmunity.ResetGemStaffKillImmunity(expected.Handle, lifecycle.GemStaffUpdateCount);
        return true;
    }
}
