using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 projectile simulation slices that already have enough runtime/world
/// state to execute without inventing missing gameplay behavior. The supported set currently includes Wooden,
/// Fire, Unholy, and Jester's Arrows, Bullet, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,
/// Poisoned Knife, and Bone Dagger (aiStyle 2).
/// including their generic tile-impact Kill() path. Server-owned simulation is allowed only when its committed
/// movement sweep cannot reach a source-backed CutTiles candidate; irreversible KillTile/drop effects remain a
/// separate world-effect slice. Entity damage and visual-only rotation/dust/sound also remain separate systems.
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

        // Green Laser type 20 has an owner-gated AI_001 branch. On a dedicated server owner 255 equals
        // Main.myPlayer, so vanilla mutates knockBack/localAI and later damage/penetrate via RNG. Those lifecycle
        // fields are not yet modeled here; rejecting server-owned type 20 prevents silent authoritative divergence.
        if (current.Type == VanillaProjectileIds.GreenLaser &&
            VanillaProjectileOwnership.IsServerOwned(current.Spawner))
        {
            next = default;
            return false;
        }

        bool isThrown = definition.AiStyle == VanillaProjectileAiStyles.Thrown;
        bool isBasicAiStyleOne =
            definition.AiStyle == VanillaProjectileAiStyles.Arrow &&
            (current.Type == VanillaProjectileIds.WoodenArrowFriendly ||
             current.Type == VanillaProjectileIds.FireArrow ||
             current.Type == VanillaProjectileIds.UnholyArrow ||
             current.Type == VanillaProjectileIds.JestersArrow ||
             current.Type == VanillaProjectileIds.Bullet ||
             current.Type == VanillaProjectileIds.GreenLaser);
        if (!isThrown && !isBasicAiStyleOne)
        {
            next = default;
            return false;
        }

        // TerrariaServer AI_001 uses ai[2] as a feature selector for several special aiStyle-1 families. The
        // source-backed Wooden/Fire/Unholy/Jester/Bullet/player-owned-GreenLaser path has ai[2] == 0; non-default
        // feature state remains separate.
        if (isBasicAiStyleOne && current.Ai.Ai2 != 0f)
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
            // TerrariaServer 1.4.5.8 Projectile.AI_001(), source-backed basic aiStyle-1 world-motion path.
            // Green Laser's extra branch changes scale and, only for Main.myPlayer, combat/lifecycle fields; player-owned
            // projectiles on the dedicated server skip that owner-gated mutation. Nonzero ai[2] stays separate.
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

        // Terraria evaluates Fire Arrow's type-2 extinguish branch against the PREVIOUS update's wet flag,
        // after raising current lavaWet but before replacing wet with this update's WetCollision result. This
        // ordering means the first water-contact update slows immediately but transforms only on the following
        // update. The liquid-kind flags are persistent Projectile fields and therefore remain runtime lifecycle.
        ProjectileLiquidState liquid = projectile.Lifecycle.Liquid;
        ProjectileTypeId outputType = current.Type;
        if (!definition.IgnoreWater)
        {
            VanillaLiquidContactState contacts = VanillaWorldCollision.GetLiquidContacts(
                tiles,
                current.PositionX,
                current.PositionY,
                definition.Width,
                definition.Height);

            bool lavaWet = liquid.LavaWet || contacts.Lava;
            bool honeyWet = liquid.HoneyWet || contacts.Honey;
            bool shimmerWet = liquid.ShimmerWet || contacts.Shimmer;

            if (current.Type == VanillaProjectileIds.FireArrow && liquid.Wet && !lavaWet)
                outputType = VanillaProjectileIds.WoodenArrowFriendly;

            liquid = new ProjectileLiquidState(
                Wet: contacts.Wet,
                LavaWet: lavaWet,
                HoneyWet: honeyWet,
                ShimmerWet: shimmerWet);
        }

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
        float movementX = collidedVelocityX;
        float movementY = collidedVelocityY;
        if (!definition.IgnoreWater && liquid.Wet)
        {
            float scale = liquid.ShimmerWet
                ? ShimmerMovementScale
                : liquid.HoneyWet
                    ? HoneyMovementScale
                    : WaterMovementScale;

            movementX = collideX ? collidedVelocityX : collidedVelocityX * scale;
            movementY = collideY ? collidedVelocityY : collidedVelocityY * scale;
        }

        float positionX = current.PositionX;
        float positionY = current.PositionY;
        if (tileImpact)
        {
            // The ordinary aiStyle-1 arrows and supported aiStyle-2 family reach HandleMovement's generic
            // collision fallback: advance by the collision-clamped velocity, Kill(), then continue into the
            // common UpdatePosition tail. Their modeled Kill branches add no authoritative world mutation here.
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
            outputType,
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
        next = new ProjectileSimulationStepResult(state, timeLeft, liquid);
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
