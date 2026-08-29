using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 projectile simulation slices that already have enough runtime/world
/// state to execute without inventing missing gameplay behavior. The first supported path is ProjectileID 3
/// (Shuriken, aiStyle 2): its deterministic AI, liquid movement, centered 6x6 tile collision, position update
/// and lifetime are reproduced here. Entity damage and visual-only rotation/dust/sound remain separate systems.
/// </summary>
internal sealed class VanillaProjectileWorldStateStepper : IProjectileStateStepper
{
    private const int ShurikenWidth = 22;
    private const int ShurikenHeight = 22;
    private const int ShurikenCollisionWidth = 6;
    private const int ShurikenCollisionHeight = 6;
    private const float ShurikenCollisionOffsetX = (ShurikenWidth - ShurikenCollisionWidth) * 0.5f;
    private const float ShurikenCollisionOffsetY = (ShurikenHeight - ShurikenCollisionHeight) * 0.5f;
    private const float WaterMovementScale = 0.5f;
    private const float HoneyMovementScale = 0.25f;
    private const float ShimmerMovementScale = 0.375f;
    private const float MaximumFallSpeed = 32f;

    private readonly WorldTileStore tiles;
    private readonly double worldSurfaceTiles;
    private bool windPhysics;
    private float windSpeedCurrent;
    private float windPhysicsStrength;

    public VanillaProjectileWorldStateStepper(WorldTileStore tiles)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        worldSurfaceTiles = tiles.WorldSurfaceTiles ?? Math.Max(1d, tiles.Dimensions.HeightTiles / 3d);
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
        if (current.Type != VanillaProjectileIds.Shuriken)
        {
            next = default;
            return false;
        }

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;

        // TerrariaServer 1.4.5.8 AI(), aiStyle == 2. ProjectileID 3 falls through the ordinary branch.
        if (windPhysics)
            velocityX += windSpeedCurrent * windPhysicsStrength;

        float ai0 = current.Ai.Ai0 + 1f;
        if (ai0 >= 20f)
        {
            velocityY += 0.4f;
            velocityX *= 0.97f;
        }

        if (velocityY > MaximumFallSpeed)
            velocityY = MaximumFallSpeed;

        // Projectile.Update performs a second wind-physics pass after AI when the projectile is above the
        // surface, in open air, and its horizontal motion meets the vanilla opposition/low-speed predicate.
        ApplyPostAiWind(current.PositionX, current.PositionY, ref velocityX);

        bool wet = VanillaWorldCollision.TryGetWetContact(
            tiles,
            current.PositionX,
            current.PositionY,
            ShurikenWidth,
            ShurikenHeight,
            out WorldLiquidKind liquidKind);

        VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
            tiles,
            current.PositionX + ShurikenCollisionOffsetX,
            current.PositionY + ShurikenCollisionOffsetY,
            velocityX,
            velocityY,
            ShurikenCollisionWidth,
            ShurikenCollisionHeight,
            fallThrough: true,
            fall2: true);

        float collidedVelocityX = collision.VelocityX;
        float collidedVelocityY = collision.VelocityY;
        bool collideX = collidedVelocityX != velocityX;
        bool collideY = collidedVelocityY != velocityY;

        float movementX = collidedVelocityX;
        float movementY = collidedVelocityY;
        if (wet)
        {
            float scale = liquidKind switch
            {
                WorldLiquidKind.Honey => HoneyMovementScale,
                WorldLiquidKind.Shimmer => ShimmerMovementScale,
                _ => WaterMovementScale
            };

            movementX = collideX ? collidedVelocityX : collidedVelocityX * scale;
            movementY = collideY ? collidedVelocityY : collidedVelocityY * scale;
        }

        bool tileImpact = collideX || collideY;
        float positionX = current.PositionX;
        float positionY = current.PositionY;
        if (tileImpact)
        {
            // Generic HandleMovement collision handling for aiStyle 2 advances by the clamped velocity before
            // Kill(), then the method still reaches UpdatePosition. Kill side effects for Shuriken are visual.
            positionX += collidedVelocityX;
            positionY += collidedVelocityY;
        }

        positionX += movementX;
        positionY += movementY;

        var state = new ProjectileStateUpdate(
            current.Type,
            current.Spawner,
            positionX,
            positionY,
            collidedVelocityX,
            collidedVelocityY,
            new ProjectileAiState(ai0, current.Ai.Ai1, current.Ai.Ai2),
            current.BannerIdToRespondTo,
            current.Damage,
            current.KnockBack,
            current.OriginalDamage);

        int timeLeft = tileImpact ? 0 : projectile.Lifecycle.TimeLeft - 1;
        next = new ProjectileSimulationStepResult(state, timeLeft);
        return true;
    }

    private void ApplyPostAiWind(float positionX, float positionY, ref float velocityX)
    {
        if (!windPhysics)
            return;

        float centerX = positionX + ShurikenWidth * 0.5f;
        float centerY = positionY + ShurikenHeight * 0.5f;
        if ((double)centerY >= worldSurfaceTiles * 16d)
            return;

        int tileX = (int)(centerX / 16f);
        int tileY = (int)(centerY / 16f);
        if ((uint)tileX >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)tileY >= (uint)tiles.Dimensions.HeightTiles ||
            tiles.Get(tileX, tileY).Wall != 0)
        {
            return;
        }

        float windDelta = windSpeedCurrent * windPhysicsStrength;
        bool opposingOrSlow =
            (velocityX > 0f && windSpeedCurrent < 0f) ||
            (velocityX < 0f && windSpeedCurrent > 0f) ||
            Math.Abs(velocityX) < Math.Abs(windDelta) * 180f;

        if (opposingOrSlow && Math.Abs(velocityX) < 16f)
            velocityX += windDelta;
    }
}
