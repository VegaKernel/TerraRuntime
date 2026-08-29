using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 projectile simulation slices that already have enough runtime/world
/// state to execute without inventing missing gameplay behavior. The supported player-owned aiStyle 2 family
/// currently includes Shuriken, Throwing Knife, Poisoned Knife, and Bone Dagger: deterministic AI, liquid
/// movement, source-backed tile collision, position update, and lifetime are reproduced here. Entity damage
/// and visual-only rotation/dust/sound remain separate systems.
/// </summary>
internal sealed class VanillaProjectileWorldStateStepper : IProjectileStateStepper
{
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
        if (!VanillaProjectileDefinitionCatalog.TryGet(current.Type, out VanillaProjectileDefinition definition) ||
            definition.AiStyle != VanillaProjectileAiStyles.Thrown)
        {
            next = default;
            return false;
        }

        // TerrariaServer 1.4.5.8 CanCutTiles() is true for this aiStyle 2 family. On a dedicated server that
        // mutation path executes for owner == Main.myPlayer (255). TerraRuntime does not yet have a projectile
        // world-mutation effect sink, so only definitions that actually carry that side effect need this gate.
        if (definition.CanCutTiles && VanillaProjectileOwnership.IsServerOwned(current.Spawner))
        {
            next = default;
            return false;
        }

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;

        // TerrariaServer 1.4.5.8 AI(), aiStyle == 2. The supported family shares this deterministic path.
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
        ApplyPostAiWind(in definition, current.PositionX, current.PositionY, ref velocityX);

        WorldLiquidKind liquidKind = default;
        bool wet = !definition.IgnoreWater && VanillaWorldCollision.TryGetWetContact(
            tiles,
            current.PositionX,
            current.PositionY,
            definition.Width,
            definition.Height,
            out liquidKind);

        float collidedVelocityX = velocityX;
        float collidedVelocityY = velocityY;
        bool collideX = false;
        bool collideY = false;
        if (definition.TileCollide)
        {
            VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
                tiles,
                current.PositionX + definition.CollisionOffsetX,
                current.PositionY + definition.CollisionOffsetY,
                velocityX,
                velocityY,
                definition.CollisionWidth,
                definition.CollisionHeight,
                fallThrough: true,
                fall2: true);

            collidedVelocityX = collision.VelocityX;
            collidedVelocityY = collision.VelocityY;
            collideX = collidedVelocityX != velocityX;
            collideY = collidedVelocityY != velocityY;
        }

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
            // Kill(), then the method still reaches UpdatePosition. Kill side effects for this family are visual.
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

    private void ApplyPostAiWind(
        in VanillaProjectileDefinition definition,
        float positionX,
        float positionY,
        ref float velocityX)
    {
        if (!windPhysics)
            return;

        float centerX = positionX + definition.Width * 0.5f;
        float centerY = positionY + definition.Height * 0.5f;
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
