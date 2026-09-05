using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 liquid displacement rules for the ordinary player collision path.
/// Liquid movement scales the position advance, not the persisted collision velocity. If tile collision
/// changes one velocity axis, that clamped axis is advanced without the liquid scale while the unchanged
/// axis keeps the liquid movement scale.
/// </summary>
internal static class VanillaServerPlayerLiquidMovement
{
    internal const float WaterMovementScale = 0.5f;
    internal const float LavaMovementScale = 0.5f;
    internal const float HoneyMovementScale = 0.25f;
    internal const float ShimmerMovementScale = 0.375f;

    public static float ResolveMovementScale(in VanillaLiquidContactState contacts)
    {
        if (contacts.Shimmer)
            return ShimmerMovementScale;
        if (contacts.Honey)
            return HoneyMovementScale;
        if (contacts.Wet)
            return contacts.Lava ? LavaMovementScale : WaterMovementScale;

        return 1f;
    }

    public static VanillaServerPlayerLiquidDisplacement ResolveDisplacement(
        float preCollisionVelocityX,
        float preCollisionVelocityY,
        float collisionVelocityX,
        float collisionVelocityY,
        float movementScale)
    {
        if (!float.IsFinite(preCollisionVelocityX) ||
            !float.IsFinite(preCollisionVelocityY) ||
            !float.IsFinite(collisionVelocityX) ||
            !float.IsFinite(collisionVelocityY) ||
            !float.IsFinite(movementScale) ||
            movementScale <= 0f ||
            movementScale > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(movementScale));
        }

        float movementX = collisionVelocityX * movementScale;
        float movementY = collisionVelocityY * movementScale;

        if (preCollisionVelocityX != collisionVelocityX)
            movementX = collisionVelocityX;
        if (preCollisionVelocityY != collisionVelocityY)
            movementY = collisionVelocityY;

        return new VanillaServerPlayerLiquidDisplacement(movementX, movementY);
    }
}

internal readonly record struct VanillaServerPlayerLiquidDisplacement(
    float X,
    float Y);
