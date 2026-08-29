using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 projectile simulation slices that already have enough runtime/world
/// state to execute without inventing missing gameplay behavior. The supported set currently includes Wooden
/// Arrow free flight (aiStyle 1) plus Shuriken, Throwing Knife, Poisoned Knife, and Bone Dagger (aiStyle 2).
/// Wooden Arrow tile-impact Kill() effects remain an explicit unsupported boundary. Server-owned simulation is
/// allowed only when its committed movement sweep cannot reach a source-backed CutTiles candidate; irreversible
/// KillTile/drop effects remain a separate world-effect slice. Entity damage and visual-only rotation/dust/sound
/// also remain separate systems.
/// </summary>
internal sealed class VanillaProjectileWorldStateStepper : IProjectileStateStepper
{
    private const float WaterMovementScale = 0.5f;
    private const float HoneyMovementScale = 0.25f;
    private const float ShimmerMovementScale = 0.375f;
    private const float MaximumThrownFallSpeed = 32f;
    private const float MaximumArrowFallSpeed = 16f;

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
        if (!VanillaProjectileDefinitionCatalog.TryGet(current.Type, out VanillaProjectileDefinition definition))
        {
            next = default;
            return false;
        }

        bool isThrown = definition.AiStyle == VanillaProjectileAiStyles.Thrown;
        bool isWoodenArrow =
            current.Type == VanillaProjectileIds.WoodenArrowFriendly &&
            definition.AiStyle == VanillaProjectileAiStyles.Arrow;
        if (!isThrown && !isWoodenArrow)
        {
            next = default;
            return false;
        }

        // TerrariaServer AI_001 uses ai[2] as a feature selector for several special arrow families. The
        // ordinary Wooden Arrow path has ai[2] == 0; non-default feature state remains a separate slice.
        if (isWoodenArrow && current.Ai.Ai2 != 0f)
        {
            next = default;
            return false;
        }

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;

        if (isThrown)
        {
            // TerrariaServer 1.4.5.8 AI(), aiStyle == 2. The supported family shares this deterministic path.
            if (windPhysics)
                velocityX += windSpeedCurrent * windPhysicsStrength;

            ai0 += 1f;
            if (ai0 >= 20f)
            {
                velocityY += 0.4f;
                velocityX *= 0.97f;
            }

            if (velocityY > MaximumThrownFallSpeed)
                velocityY = MaximumThrownFallSpeed;
        }
        else
        {
            // TerrariaServer 1.4.5.8 Projectile.AI_001(), ordinary type-1 Wooden Arrow path. Type-specific
            // homing, dust, sound, feature and kill branches do not apply when ai[2] is the default zero value.
            ai0 += 1f;
            if (ai0 >= 15f)
            {
                ai0 = 15f;
                velocityY += 0.1f;
            }

            if (velocityY > MaximumArrowFallSpeed)
                velocityY = MaximumArrowFallSpeed;
        }

        // Projectile.Update performs a wind-physics pass after AI when the projectile is above the surface,
        // in open air, and its horizontal motion meets the vanilla opposition/low-speed predicate.
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

        bool tileImpact = collideX || collideY;
        if (isWoodenArrow && tileImpact)
        {
            // Vanilla routes this through projectile collision handling and Kill(). Type-1 recovery/drop and
            // other kill side effects are not source-pinned in TerraRuntime yet, so do not commit a partial hit.
            next = default;
            return false;
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

        float positionX = current.PositionX;
        float positionY = current.PositionY;
        if (isThrown && tileImpact)
        {
            // Generic HandleMovement collision handling for aiStyle 2 advances by the clamped velocity before
            // Kill(), then the method still reaches UpdatePosition. Kill side effects for this family are visual.
            positionX += collidedVelocityX;
            positionY += collidedVelocityY;
        }

        positionX += movementX;
        positionY += movementY;

        // TerrariaServer reaches CutTiles for owner == Main.myPlayer (255). Until KillTile/drop effects are
        // modeled, server-owned movement is safe only when a conservative sweep proves that no source-backed
        // CutTilesAt candidate can be reached. The sweep is intentionally a superset of vanilla's rectangle:
        // an extra rejection is harmless, while missing a candidate would silently lose an irreversible mutation.
        if (definition.CanCutTiles &&
            VanillaProjectileOwnership.IsServerOwned(current.Spawner) &&
            VanillaWorldProjectileTileCut.HasCandidateAlongSweep(
                tiles,
                current.PositionX,
                current.PositionY,
                positionX,
                positionY,
                definition.Width,
                definition.Height))
        {
            next = default;
            return false;
        }

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

        int timeLeft = isThrown && tileImpact ? 0 : projectile.Lifecycle.TimeLeft - 1;
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
