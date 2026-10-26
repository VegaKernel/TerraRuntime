using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct ToolProjectileTerrain1458(float X, float Y,
    float IncomingX, float IncomingY, float ClampedX, float ClampedY,
    float WetVelocityX, float WetVelocityY, ProjectileLiquidState Liquid, bool HasImpact)
{
    internal byte WetCount { get; init; }
    internal byte ShimmerColourDraws { get; init; }
}

internal sealed partial class VanillaProjectileWorldMotionResolver
{
    // Source HandleMovement only damps wet velocity inside its tile-collision
    // branch. The23 noncolliding held bodies retain their full motion in water.
    internal bool TryCaptureToolTerrain(ProjectileTypeId type, float x, float y,
        float vx, float vy, bool tileCollide, in ProjectileLiquidState before,
        out ToolProjectileTerrain1458 terrain) =>
        TryCaptureToolTerrain(type, x, y, vx, vy, tileCollide, in before, 0, out terrain);

    internal bool TryCaptureToolTerrain(ProjectileTypeId type, float x, float y,
        float vx, float vy, bool tileCollide, in ProjectileLiquidState before,
        byte beforeWetCount, out ToolProjectileTerrain1458 terrain)
    {
        terrain = default;
        bool bobber = TerraRuntime.Gameplay.Players.FishingRules1458.IsBobber(type.Value);
        if (!(bobber ? VanillaDefinitionCatalog.TryGet(type, out var definition) : VanillaToolProjectileDefinition1458.TryGet(type, out definition)) ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(vx) || !float.IsFinite(vy) ||
            tileCollide && !bobber && type.Value != VanillaThrownAxeProjectile1458.ProjectileType)
            return false;
        var liquid = before;
        byte wetCount = beforeWetCount, colourDraws = 0;
        if (!definition.IgnoreWater)
        {
            var contact = VanillaWorldCollision.GetLiquidContacts(tiles, x, y, definition.Width, definition.Height);
            bool shimmer = before.ShimmerWet || contact.Shimmer;
            int cooldown = beforeWetCount;
            if (contact.Wet)
            {
                if (!before.Wet && cooldown == 0 && !contact.Lava && shimmer) colourDraws = 10;
                liquid = new(true, before.LavaWet || contact.Lava, before.HoneyWet || contact.Honey, shimmer);
            }
            else
            {
                if (before.Wet && cooldown == 0)
                {
                    cooldown = 10;
                    if (!before.LavaWet && before.ShimmerWet) colourDraws = 10;
                }
                // Source clears every retained liquid kind on the first dry frame.
                liquid = default;
            }
            wetCount = (byte)Math.Max(0, cooldown - 1);
        }
        float resolvedX = vx, resolvedY = vy, wetX = vx, wetY = vy;
        bool changedPosition = false;
        if (tileCollide)
        {
            float collisionX = x + definition.CollisionOffsetX;
            float collisionY = y + definition.CollisionOffsetY;
            var collision = VanillaWorldCollision.TileCollision(tiles, collisionX, collisionY,
                vx, vy, definition.CollisionWidth, definition.CollisionHeight,
                fallThrough: true, fall2: true);
            resolvedX = collision.VelocityX;
            resolvedY = collision.VelocityY;
            if (liquid.Wet)
            {
                float scale = liquid.ShimmerWet ? .375f : liquid.HoneyWet ? .25f : .5f;
                wetX = resolvedX * scale;
                wetY = resolvedY * scale;
                if (resolvedX != vx) wetX = resolvedX;
                if (resolvedY != vy) wetY = resolvedY;
                var slope = VanillaWorldSlopeCollision.Resolve(tiles, collisionX, collisionY,
                    resolvedX, resolvedY, definition.CollisionWidth, definition.CollisionHeight, fall: true);
                changedPosition = slope.PositionX != collisionX || slope.PositionY != collisionY;
                x += slope.PositionX - collisionX;
                y += slope.PositionY - collisionY;
                resolvedX = slope.VelocityX;
                resolvedY = slope.VelocityY;
            }
            else if (MathF.Sqrt(vx * vx + vy * vy) >
                Math.Clamp(Math.Min(definition.CollisionWidth, definition.CollisionHeight), 3, 16))
            {
                float beforeX = x, beforeY = y;
                ResolveEclipseDryCollision(in definition, vx, vy, ref x, ref y, ref resolvedX, ref resolvedY);
                changedPosition = x != beforeX || y != beforeY;
            }
            else
            {
                var slope = VanillaWorldSlopeCollision.Resolve(tiles, collisionX, collisionY,
                    resolvedX, resolvedY, definition.CollisionWidth, definition.CollisionHeight, fall: true);
                changedPosition = slope.PositionX != collisionX || slope.PositionY != collisionY;
                x += slope.PositionX - collisionX;
                y += slope.PositionY - collisionY;
                resolvedX = slope.VelocityX;
                resolvedY = slope.VelocityY;
            }
        }
        // Actual ShouldUseWindPhysics is false for every one of these24 types,
        // including explicit enabled wind of either sign in the source capsule.
        // AI3 is exempt from the generic early bounds Kill. While outbound,
        // HandleMovement routes world-edge contact through its ordinary impact
        // branch even when TileCollision returned unchanged velocity.
        bool sourceEdgeImpact = tileCollide && type.Value == VanillaThrownAxeProjectile1458.ProjectileType &&
            (x <= 0f || y <= 0f || x + definition.Width >= tiles.Dimensions.WidthTiles * 16f ||
             y + definition.Height >= tiles.Dimensions.HeightTiles * 16f);
        terrain = new(x, y, vx, vy, resolvedX, resolvedY, wetX, wetY, liquid,
            sourceEdgeImpact || changedPosition || resolvedX != vx || resolvedY != vy)
        { WetCount = wetCount, ShimmerColourDraws = colourDraws };
        return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(resolvedX) && float.IsFinite(resolvedY);
    }

    internal static (float X, float Y) CompleteToolPosition(in ToolProjectileTerrain1458 terrain,
        float afterImpactVelocityX, float afterImpactVelocityY) =>
        (terrain.X + (terrain.Liquid.Wet ? terrain.WetVelocityX : afterImpactVelocityX),
         terrain.Y + (terrain.Liquid.Wet ? terrain.WetVelocityY : afterImpactVelocityY));
}
