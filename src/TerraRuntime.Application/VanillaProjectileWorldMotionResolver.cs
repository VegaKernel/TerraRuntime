using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Resolves the world-dependent half of a supported vanilla projectile update after AI-family behavior has run.
/// This layer owns post-AI wind, liquid contact/state, tile collision, position integration, generic impact expiry
/// and the conservative CutTiles side-effect boundary. It does not decide AI-family velocity changes.
/// </summary>
internal sealed class VanillaProjectileWorldMotionResolver
{
    private const float WaterMovementScale = 0.5f;
    private const float HoneyMovementScale = 0.25f;
    private const float ShimmerMovementScale = 0.375f;

    private readonly WorldTileStore tiles;
    private readonly double worldSurfaceTiles;

    public VanillaProjectileWorldMotionResolver(WorldTileStore tiles)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        worldSurfaceTiles = tiles.WorldSurfaceTiles ?? Math.Max(1d, tiles.Dimensions.HeightTiles / 3d);
    }

    public int WorldWidthPixels => checked(tiles.Dimensions.WidthTiles * 16);

    public int WorldHeightPixels => checked(tiles.Dimensions.HeightTiles * 16);

    public bool TryResolve(
        in ProjectileSimulationStepContext projectile,
        in VanillaProjectileDefinition definition,
        in VanillaProjectileBehaviorResult behavior,
        in VanillaProjectileBehaviorContext behaviorContext,
        out ProjectileSimulationStepResult next)
    {
        ProjectileSnapshot current = projectile.Projectile;
        float velocityX = behavior.VelocityX;
        float velocityY = behavior.VelocityY;
        float behaviorPositionX = behavior.PositionXOverride ?? current.PositionX;
        float behaviorPositionY = behavior.PositionYOverride ?? current.PositionY;

        if (current.Type == VanillaProjectileIds.SkeletronPrimeBomb && IsPrimeBombPlatformContact(in current, in definition))
        {
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type, current.Spawner, behaviorPositionX, behaviorPositionY, velocityX, velocityY,
                    new ProjectileAiState(behavior.Ai0, behavior.Ai1Override ?? current.Ai.Ai1, current.Ai.Ai2),
                    current.BannerIdToRespondTo, current.Damage, current.KnockBack, current.OriginalDamage),
                TimeLeft: 0,
                Liquid: projectile.Lifecycle.Liquid,
                TerminationReason: ProjectileSimulationTerminationReason.BehaviorKill);
            return true;
        }

        if (behavior.Kill)
        {
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type,
                    current.Spawner,
                    behaviorPositionX,
                    behaviorPositionY,
                    velocityX,
                    velocityY,
                    new ProjectileAiState(behavior.Ai0, behavior.Ai1Override ?? current.Ai.Ai1, current.Ai.Ai2),
                    current.BannerIdToRespondTo,
                    current.Damage,
                    current.KnockBack,
                    current.OriginalDamage),
                TimeLeft: 0,
                Liquid: projectile.Lifecycle.Liquid,
                TerminationReason: ProjectileSimulationTerminationReason.BehaviorKill);
            return true;
        }

        // Projectile.Update performs this wind-physics pass after AI when the projectile is above the surface,
        // in open air, and horizontal motion meets the vanilla opposition/low-speed predicate.
        ApplyPostAiWind(in definition, behaviorPositionX, behaviorPositionY, in behaviorContext, ref velocityX);

        // Fire Arrow evaluates its extinguish branch against the PREVIOUS update's wet flag. Current lava/honey/
        // shimmer flags are raised before wet is replaced by this update's contact result.
        ProjectileLiquidState liquid = projectile.Lifecycle.Liquid;
        ProjectileTypeId outputType = current.Type;
        if (!definition.IgnoreWater)
        {
            VanillaLiquidContactState contacts = VanillaWorldCollision.GetLiquidContacts(
                tiles,
                behaviorPositionX,
                behaviorPositionY,
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

        float resolvedAi0 = behavior.Ai0;
        float collidedVelocityX = velocityX;
        float collidedVelocityY = velocityY;
        bool collideX = false;
        bool collideY = false;
        bool tileCollide = behavior.TileCollideOverride ?? definition.TileCollide;
        if (tileCollide)
        {
            VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
                tiles,
                behaviorPositionX + definition.CollisionOffsetX,
                behaviorPositionY + definition.CollisionOffsetY,
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
        bool bombCollisionHandled = false;
        bool rocketArmedByImpact = false;
        if (tileImpact && definition.AiStyle == VanillaProjectileAiStyles.Bomb && current.Type.Value is >= 133 and <= 144)
        {
            // Projectile.Update aiStyle-16 tile collision: launcher grenades/mines bounce at 40% of the incoming
            // component. Straight rocket variants stop, hide and arm a 3-tick fuse instead of dying immediately.
            if (collideX)
                collidedVelocityX = velocityX * -0.4f;
            if (collideY && velocityY > 0.7f)
                collidedVelocityY = velocityY * -0.4f;

            if ((current.Type.Value - 133) % 3 == 1)
            {
                collidedVelocityX = 0f;
                collidedVelocityY = 0f;
                rocketArmedByImpact = true;
            }

            bombCollisionHandled = true;
        }

        bool thornBallCollisionHandled = false;
        if (tileImpact && current.Type == VanillaProjectileIds.PlanteraThornBall &&
            definition.AiStyle == VanillaProjectileAiStyles.BouncyBall)
        {
            // Projectile.Update aiStyle-14 type 277 rebounds horizontal impacts at 90%. Downward impacts
            // above 3 px/update rebound Y at 90%; gentler vertical contacts retain the collision-clamped Y.
            if (collideX)
                collidedVelocityX = velocityX * -0.9f;
            if (collideY && velocityY > 3f)
                collidedVelocityY = velocityY * -0.9f;
            thornBallCollisionHandled = true;
        }

        bool rainbowRodControlledCollisionHandled = false;
        if (tileImpact && current.Type == VanillaProjectileIds.RainbowRodBullet &&
            definition.AiStyle == VanillaProjectileAiStyles.MagicMissile && resolvedAi0 >= 0f)
        {
            // AI_009 type 79 is the one modern controlled-magic exception that survives tile contact while
            // channelled. Projectile.Update damps only the collided components to 10% of the incoming velocity.
            if (collideX)
                collidedVelocityX = velocityX * 0.1f;
            if (collideY)
                collidedVelocityY = velocityY * 0.1f;
            rainbowRodControlledCollisionHandled = true;
        }

        bool golemFireballCollisionHandled = false;
        bool golemFireballKilledByImpact = false;
        if (tileImpact && current.Type == VanillaProjectileIds.GolemFireball &&
            definition.AiStyle == VanillaProjectileAiStyles.Fireball)
        {
            // aiStyle-8 tile collision increments ai[0]. Type 258 bounces changed components at full magnitude
            // for impacts 1..4; the fifth impact kills after applying the collision-clamped motion.
            resolvedAi0 += 1f;
            if (resolvedAi0 >= 5f)
            {
                golemFireballKilledByImpact = true;
            }
            else
            {
                if (collideX)
                    collidedVelocityX = -velocityX;
                if (collideY)
                    collidedVelocityY = -velocityY;
            }
            golemFireballCollisionHandled = true;
        }

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

        float positionX = behaviorPositionX;
        float positionY = behaviorPositionY;
        if (tileImpact && !bombCollisionHandled && !golemFireballCollisionHandled && !thornBallCollisionHandled && !rainbowRodControlledCollisionHandled)
        {
            // Supported aiStyle-1/2 families use the generic impact fallback: movement first advances by the
            // collision-clamped velocity, Kill() expires the projectile, then UpdatePosition reaches its common tail.
            positionX += collidedVelocityX;
            positionY += collidedVelocityY;
        }

        positionX += movementX;
        positionY += movementY;

        // Dedicated-server ownership reaches CutTiles. Until irreversible KillTile/drop effects are modeled,
        // server-owned simulation is accepted only when a conservative sweep proves no candidate is reachable.
        if (definition.CanCutTiles &&
            VanillaProjectileOwnership.IsServerOwned(current.Spawner) &&
            VanillaWorldProjectileTileCut.HasCandidateAlongSweep(
                tiles,
                behaviorPositionX,
                behaviorPositionY,
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
            new ProjectileAiState(resolvedAi0, behavior.Ai1Override ?? current.Ai.Ai1, current.Ai.Ai2),
            current.BannerIdToRespondTo,
            current.Damage,
            current.KnockBack,
            current.OriginalDamage);

        int timeLeft;
        ProjectileSimulationTerminationReason terminationReason;
        int sourceTimeLeft = behavior.TimeLeftOverride.HasValue
            ? Math.Min(projectile.Lifecycle.TimeLeft, behavior.TimeLeftOverride.Value)
            : projectile.Lifecycle.TimeLeft;
        if (behavior.MinimumTimeLeftOverride.HasValue)
            sourceTimeLeft = Math.Max(sourceTimeLeft, behavior.MinimumTimeLeftOverride.Value);
        if (bombCollisionHandled)
        {
            // The source sets straight rockets to timeLeft=3 on impact and the common Update tail performs the
            // ordinary decrement. Grenade/mine variants simply continue their existing fuse/lifetime.
            timeLeft = rocketArmedByImpact
                ? Math.Min(sourceTimeLeft, 3) - 1
                : sourceTimeLeft - 1;
            terminationReason = timeLeft <= 0
                ? ProjectileSimulationTerminationReason.LifetimeExpired
                : ProjectileSimulationTerminationReason.None;
        }
        else if (golemFireballCollisionHandled)
        {
            timeLeft = golemFireballKilledByImpact ? 0 : sourceTimeLeft - 1;
            terminationReason = golemFireballKilledByImpact
                ? ProjectileSimulationTerminationReason.TileCollision
                : timeLeft <= 0
                    ? ProjectileSimulationTerminationReason.LifetimeExpired
                    : ProjectileSimulationTerminationReason.None;
        }
        else if (rainbowRodControlledCollisionHandled || thornBallCollisionHandled)
        {
            timeLeft = sourceTimeLeft - 1;
            terminationReason = timeLeft <= 0
                ? ProjectileSimulationTerminationReason.LifetimeExpired
                : ProjectileSimulationTerminationReason.None;
        }
        else
        {
            timeLeft = tileImpact ? 0 : sourceTimeLeft - 1;
            terminationReason = tileImpact
                ? ProjectileSimulationTerminationReason.TileCollision
                : timeLeft <= 0
                    ? ProjectileSimulationTerminationReason.LifetimeExpired
                    : ProjectileSimulationTerminationReason.None;
        }
        next = new ProjectileSimulationStepResult(state, timeLeft, liquid, terminationReason);
        return true;
    }

    private bool IsPrimeBombPlatformContact(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition)
    {
        float centerX = projectile.PositionX + definition.Width * 0.5f;
        float centerY = projectile.PositionY + definition.Height * 0.5f;
        int tileX = (int)(centerX / 16f);
        int tileY = (int)(centerY / 16f);
        if ((uint)tileX >= (uint)tiles.Dimensions.WidthTiles || (uint)tileY >= (uint)tiles.Dimensions.HeightTiles)
            return false;

        WorldTile tile = tiles.Get(tileX, tileY);
        return tile.IsActive && !tile.IsActuated &&
               (VanillaTileIds.IsPlatform(tile.TileType) || tile.Type == 380);
    }

    private void ApplyPostAiWind(
        in VanillaProjectileDefinition definition,
        float positionX,
        float positionY,
        in VanillaProjectileBehaviorContext behaviorContext,
        ref float velocityX)
    {
        if (!behaviorContext.WindPhysics)
            return;

        float centerX = positionX + definition.Width * 0.5f;
        float centerY = positionY + definition.Height * 0.5f;
        if ((double)centerY >= worldSurfaceTiles * 16d)
            return;

        int tileX = (int)(centerX / 16f);
        int tileY = (int)(centerY / 16f);
        if ((uint)tileX >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)tileY >= (uint)tiles.Dimensions.HeightTiles ||
            tiles.Get(tileX, tileY).WallType != VanillaWallIds.None)
        {
            return;
        }

        float windDelta = behaviorContext.WindSpeedCurrent * behaviorContext.WindPhysicsStrength;
        bool opposingOrSlow =
            (velocityX > 0f && behaviorContext.WindSpeedCurrent < 0f) ||
            (velocityX < 0f && behaviorContext.WindSpeedCurrent > 0f) ||
            Math.Abs(velocityX) < Math.Abs(windDelta) * 180f;

        if (opposingOrSlow && Math.Abs(velocityX) < 16f)
            velocityX += windDelta;
    }
}
