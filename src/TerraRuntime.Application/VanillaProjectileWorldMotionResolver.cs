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
        if (tileImpact)
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
            new ProjectileAiState(behavior.Ai0, behavior.Ai1Override ?? current.Ai.Ai1, current.Ai.Ai2),
            current.BannerIdToRespondTo,
            current.Damage,
            current.KnockBack,
            current.OriginalDamage);

        int timeLeft = tileImpact ? 0 : projectile.Lifecycle.TimeLeft - 1;
        ProjectileSimulationTerminationReason terminationReason = tileImpact
            ? ProjectileSimulationTerminationReason.TileCollision
            : ProjectileSimulationTerminationReason.None;
        next = new ProjectileSimulationStepResult(state, timeLeft, liquid, terminationReason);
        return true;
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
