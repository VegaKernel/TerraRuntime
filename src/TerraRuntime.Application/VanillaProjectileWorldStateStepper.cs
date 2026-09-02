using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Production orchestration boundary for the source-backed TerrariaServer 1.4.5.8 projectile simulation slice.
/// Runtime behavior-family selection is explicit and separate from source-backed aiStyle metadata. World motion
/// owns wind, liquids, collision, integration and the conservative CutTiles boundary. Combat/damage and
/// irreversible world effects remain separate slices.
/// </summary>
internal sealed class VanillaProjectileWorldStateStepper : IProjectileStateStepper
{
    private readonly VanillaProjectileWorldMotionResolver worldMotion;
    private readonly IRuntimePlayerSlotSnapshotLookup? playerSnapshots;
    private bool windPhysics;
    private float windSpeedCurrent;
    private float windPhysicsStrength;

    public VanillaProjectileWorldStateStepper(
        WorldTileStore tiles,
        IRuntimePlayerSlotSnapshotLookup? playerSnapshots = null)
    {
        worldMotion = new VanillaProjectileWorldMotionResolver(
            tiles ?? throw new ArgumentNullException(nameof(tiles)));
        this.playerSnapshots = playerSnapshots;
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
        if (!VanillaProjectileDefinitionCatalog.TryGet(current.Type, out VanillaProjectileDefinition definition) ||
            !VanillaProjectileBehaviorProfileCatalog.TryGet(current.Type, out VanillaProjectileBehaviorProfile profile) ||
            definition.AiStyle != profile.ExpectedAiStyle)
        {
            next = default;
            return false;
        }

        // Projectile.Update deactivates ordinary projectiles crossing Main's inclusive world edges before AI.
        // Exceptional families opt out explicitly in runtime behavior metadata instead of leaking aiStyle checks
        // into world orchestration. WorldTileStore dimensions map to Main.rightWorld/bottomWorld through the
        // verified 16 px tile scale.
        if (!profile.ExemptFromPreAiWorldBounds && IsOutsideWorld(in current, in definition))
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
            windPhysicsStrength,
            playerSnapshots);
        if (!VanillaProjectileBehaviorStepper.TryStep(
                in current,
                in definition,
                in profile,
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
