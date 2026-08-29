using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Production orchestration boundary for the source-backed TerrariaServer 1.4.5.8 projectile simulation slice.
/// AI-family behavior is world-independent; world motion owns wind, liquids, collision, integration and the
/// conservative CutTiles boundary. Combat/damage and irreversible world effects remain separate future slices.
/// </summary>
internal sealed class VanillaProjectileWorldStateStepper : IProjectileStateStepper
{
    private readonly VanillaProjectileWorldMotionResolver worldMotion;
    private bool windPhysics;
    private float windSpeedCurrent;
    private float windPhysicsStrength;

    public VanillaProjectileWorldStateStepper(WorldTileStore tiles)
    {
        worldMotion = new VanillaProjectileWorldMotionResolver(
            tiles ?? throw new ArgumentNullException(nameof(tiles)));
    }

    /// <summary>
    /// Supplies the vanilla Main wind inputs without making projectile simulation own world-weather policy.
    /// Disabled by default until the host has an authoritative wind source.
    /// </summary>
    public void SetWindPhysics(bool enabled, float speedCurrent, float strength)
    {
        if (!float.IsFinite(speedCurrent))
            throw new ArgumentOutOfRangeException(nameof(speedCurrent));
        if (!float.IsFinite(strength))
            throw new ArgumentOutOfRangeException(nameof(strength));

        windPhysics = enabled;
        windSpeedCurrent = speedCurrent;
        windPhysicsStrength = strength;
    }

    public bool TryStepState(
        in ProjectileSimulationStepContext projectile,
        out ProjectileSimulationStepResult next)
    {
        ProjectileSnapshot current = projectile.Projectile;
        if (!VanillaProjectileDefinitionCatalog.TryGet(current.Type, out VanillaProjectileDefinition definition))
        {
            next = default;
            return false;
        }

        // Projectile.Update deactivates non-boomerang projectiles crossing Main's inclusive world edges before AI.
        // WorldTileStore dimensions map to Main.rightWorld/bottomWorld through the verified 16 px tile scale.
        if (definition.AiStyle != VanillaProjectileAiStyles.Boomerang && IsOutsideWorld(in current, in definition))
        {
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type,
                    current.Spawner,
                    current.PositionX,
                    current.PositionY,
                    current.VelocityX,
                    current.VelocityY,
                    current.Ai,
                    current.BannerIdToRespondTo,
                    current.Damage,
                    current.KnockBack,
                    current.OriginalDamage),
                TimeLeft: 0,
                Liquid: projectile.Lifecycle.Liquid,
                TerminationReason: ProjectileSimulationTerminationReason.WorldBounds);
            return true;
        }

        var behaviorContext = new VanillaProjectileBehaviorContext(
            windPhysics,
            windSpeedCurrent,
            windPhysicsStrength);
        if (!VanillaProjectileBehaviorStepper.TryStep(
                in current,
                in definition,
                in behaviorContext,
                out VanillaProjectileBehaviorResult behavior))
        {
            next = default;
            return false;
        }

        return worldMotion.TryResolve(
            in projectile,
            in definition,
            in behavior,
            in behaviorContext,
            out next);
    }

    private bool IsOutsideWorld(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition) =>
        projectile.PositionX <= 0f ||
        projectile.PositionX + definition.Width >= worldMotion.WorldWidthPixels ||
        projectile.PositionY <= 0f ||
        projectile.PositionY + definition.Height >= worldMotion.WorldHeightPixels;
}
