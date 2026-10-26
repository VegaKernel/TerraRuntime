using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class VanillaProjectileWorldStateStepper
{
    internal bool TryStepWorldToolBombState(in ProjectileSimulationStepContext projectile,
        out ProjectileSimulationStepResult next, out bool sourceTileImpact)
    {
        next = default; sourceTileImpact = false;
        var current = projectile.Projectile;
        if (!VanillaWorldToolBomb1458.IsSupported(current.Type) || current.Spawner != byte.MaxValue ||
            !projectile.Lifecycle.NpcProjectile || projectile.Lifecycle.WorldToolBombKillInProgress ||
            projectile.Lifecycle.TimeLeft <= VanillaWorldToolBomb1458.FuseThreshold ||
            !VanillaDefinitionCatalog.TryGet(current.Type, out var definition) ||
            !VanillaProjectileBehaviorProfileCatalog.TryGet(current.Type, out var profile) ||
            definition.AiStyle != profile.ExpectedAiStyle) return false;
        var context = new VanillaProjectileBehaviorContext(windPhysics, windSpeedCurrent, windPhysicsStrength,
            playerSnapshots, expertMode, npcTargets, hostilePlayerTargets, projectile.Lifecycle.TimeLeft,
            projectile.Lifecycle.LocalAi, projectile.Lifecycle.Liquid.Wet);
        return VanillaProjectileBehaviorStepper.TryStep(in current, in definition, in profile, in context, out var behavior) &&
            worldMotion.TryResolve(in projectile, in definition, in behavior, in context, out next, out sourceTileImpact);
    }
}
