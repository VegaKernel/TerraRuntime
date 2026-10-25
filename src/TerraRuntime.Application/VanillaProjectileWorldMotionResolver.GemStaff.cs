using TerraRuntime.World;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal readonly record struct GemStaffTerrain1458(
    GemStaffMotionState1458 Pose,
    float IncomingVelocityX,
    float IncomingVelocityY,
    float ClampedVelocityX,
    float ClampedVelocityY,
    float WetVelocityX,
    float WetVelocityY,
    ProjectileLiquidState Liquid,
    bool HasImpact);

internal sealed partial class VanillaProjectileWorldMotionResolver
{
    // AI29's physical body is 10x10, or 22x22 after Kill's area-damage restoration.
    public bool TryCaptureGemStaffTerrain(in GemStaffMotionState1458 aiPose,
        int physicalWidth, int physicalHeight, in ProjectileLiquidState beforeLiquid,
        in VanillaProjectileBehaviorContext wind, out GemStaffTerrain1458 terrain)
    {
        terrain = default;
        if (physicalWidth != physicalHeight || physicalWidth is not (10 or 22) ||
            !float.IsFinite(aiPose.PositionX) || !float.IsFinite(aiPose.PositionY) ||
            !float.IsFinite(aiPose.VelocityX) || !float.IsFinite(aiPose.VelocityY)) return false;

        var definition = new VanillaProjectileDefinition(physicalWidth, physicalHeight,
            new(29), true, false, true, physicalWidth - 8, physicalHeight - 8);
        float x = aiPose.PositionX, y = aiPose.PositionY;
        float incomingX = aiPose.VelocityX, incomingY = aiPose.VelocityY;
        // All seven represented gem types have source WindPhysicsImmunity=true.
        _ = wind;
        var contact = VanillaWorldCollision.GetLiquidContacts(tiles, x, y, physicalWidth, physicalHeight);
        var liquid = new ProjectileLiquidState(contact.Wet, beforeLiquid.LavaWet || contact.Lava,
            beforeLiquid.HoneyWet || contact.Honey, beforeLiquid.ShimmerWet || contact.Shimmer);
        float collisionX = x + definition.CollisionOffsetX;
        float collisionY = y + definition.CollisionOffsetY;
        var first = VanillaWorldCollision.TileCollision(tiles, collisionX, collisionY,
            incomingX, incomingY, definition.CollisionWidth, definition.CollisionHeight,
            fallThrough: true, fall2: true);
        float resolvedX = first.VelocityX, resolvedY = first.VelocityY;
        float wetX = incomingX, wetY = incomingY;
        bool slopeImpact;
        if (liquid.Wet)
        {
            float scale = liquid.ShimmerWet ? .375f : liquid.HoneyWet ? .25f : .5f;
            wetX = resolvedX * scale;
            wetY = resolvedY * scale;
            if (resolvedX != incomingX) wetX = resolvedX;
            if (resolvedY != incomingY) wetY = resolvedY;
            var slope = VanillaWorldSlopeCollision.Resolve(tiles, collisionX, collisionY,
                resolvedX, resolvedY, definition.CollisionWidth, definition.CollisionHeight, fall: true);
            slopeImpact = slope.PositionX != collisionX || slope.PositionY != collisionY ||
                slope.VelocityX != resolvedX || slope.VelocityY != resolvedY;
            x += slope.PositionX - collisionX;
            y += slope.PositionY - collisionY;
            resolvedX = slope.VelocityX;
            resolvedY = slope.VelocityY;
        }
        else if (MathF.Sqrt(incomingX * incomingX + incomingY * incomingY) >
                 Math.Clamp(Math.Min(definition.CollisionWidth, definition.CollisionHeight), 3, 16))
        {
            float beforeX = x, beforeY = y;
            ResolveEclipseDryCollision(in definition, incomingX, incomingY,
                ref x, ref y, ref resolvedX, ref resolvedY);
            slopeImpact = x != beforeX || y != beforeY;
        }
        else
        {
            var slope = VanillaWorldSlopeCollision.Resolve(tiles, collisionX, collisionY,
                resolvedX, resolvedY, definition.CollisionWidth, definition.CollisionHeight, fall: true);
            slopeImpact = slope.PositionX != collisionX || slope.PositionY != collisionY ||
                slope.VelocityX != resolvedX || slope.VelocityY != resolvedY;
            x += slope.PositionX - collisionX;
            y += slope.PositionY - collisionY;
            resolvedX = slope.VelocityX;
            resolvedY = slope.VelocityY;
        }
        var pose = aiPose with { PositionX = x, PositionY = y,
            VelocityX = incomingX, VelocityY = incomingY };
        terrain = new(pose, incomingX, incomingY, resolvedX, resolvedY, wetX, wetY, liquid,
            slopeImpact || resolvedX != incomingX || resolvedY != incomingY);
        return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(resolvedX) && float.IsFinite(resolvedY);
    }

    public static GemStaffMotionState1458 CompleteGemStaffPosition(
        in GemStaffMotionState1458 afterImpact, in GemStaffTerrain1458 terrain) =>
        afterImpact with
        {
            PositionX = afterImpact.PositionX + (terrain.Liquid.Wet ? terrain.WetVelocityX : afterImpact.VelocityX),
            PositionY = afterImpact.PositionY + (terrain.Liquid.Wet ? terrain.WetVelocityY : afterImpact.VelocityY)
        };
}
