using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class VanillaProjectileWorldStateStepper : IProjectileInactiveLifetimeMotionStepper
{
    public bool SupportsInactiveLifetimeMotion(ProjectileTypeId type) =>
        VanillaProjectileUpdateFacts.SupportsInactiveLifetimeMotion1458(type);

    public bool TryStepInactiveLifetimeMotion(in ProjectileSimulationStepContext projectile,
        out ProjectileSimulationStepResult next)
    {
        var current = projectile.Projectile;
        if (!SupportsInactiveLifetimeMotion(current.Type) || projectile.Lifecycle.TimeLeft != 0 ||
            projectile.TerminationReason != ProjectileSimulationTerminationReason.LifetimeExpired ||
            projectile.SubupdateIndex <= 0 || projectile.SubupdateIndex >= projectile.SubupdatesPerWorldTick ||
            current.Ai.Ai2 != 0f || !VanillaDefinitionCatalog.TryGet(current.Type, out var definition))
        { next = default; return false; }

        // Projectile.Update(1458) enters the next while iteration after a late
        // lifetime Kill. AI is skipped, but wind/liquid/HandleMovement still run
        // once before the inactive return. No Damage/TTL decrement/second Kill.
        if (IsOutsideWorld(in current, in definition))
        {
            next = new(new(current.Type, current.Spawner, current.PositionX, current.PositionY,
                current.VelocityX, current.VelocityY, current.Ai, current.BannerIdToRespondTo,
                current.Damage, current.KnockBack, current.OriginalDamage), 0,
                projectile.Lifecycle.Liquid, ProjectileSimulationTerminationReason.LifetimeExpired,
                projectile.Lifecycle.LocalAi, PenetrateOverride: projectile.Lifecycle.PenetrateOverride);
            return true;
        }
        var behavior = new VanillaProjectileBehaviorResult(current.VelocityX, current.VelocityY, current.Ai.Ai0);
        var context = new VanillaProjectileBehaviorContext(windPhysics, windSpeedCurrent, windPhysicsStrength,
            playerSnapshots, expertMode, npcTargets, hostilePlayerTargets, 0,
            projectile.Lifecycle.LocalAi, projectile.Lifecycle.Liquid.Wet);
        if (!worldMotion.TryResolve(in projectile, in definition, in behavior, in context, out next)) return false;
        next = next with { TimeLeft = 0, TerminationReason = ProjectileSimulationTerminationReason.LifetimeExpired };
        return true;
    }
}
