namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaGroundFighter258AirborneInput(
    float PositionX,
    int Width,
    float VelocityX,
    float VelocityY,
    int DirectionX,
    float TargetCenterX);

public readonly record struct VanillaGroundFighter258AirborneResult(
    float VelocityX,
    int SpriteDirection);

/// <summary>
/// TerrariaServer 1.4.5.8 AI_003_Fighters movement branch for NPC type 258. While airborne it reacquires its
/// closest player, faces that direction, dampens opposing horizontal motion and accelerates toward the player's
/// center. Grounded high-target jumping remains at the world-collision boundary.
/// </summary>
public static class VanillaGroundFighter258Motion
{
    public static bool TryResolveAirborne(
        in VanillaGroundFighter258AirborneInput input,
        out VanillaGroundFighter258AirborneResult result)
    {
        if (!float.IsFinite(input.PositionX) ||
            input.Width <= 0 ||
            !float.IsFinite(input.VelocityX) ||
            !float.IsFinite(input.VelocityY) ||
            input.VelocityY == 0f ||
            input.DirectionX is not (-1 or 1) ||
            !float.IsFinite(input.TargetCenterX))
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float right = input.PositionX + input.Width;
        if (input.TargetCenterX < input.PositionX && velocityX > 0f)
            velocityX *= .95f;
        else if (input.TargetCenterX > right && velocityX < 0f)
            velocityX *= .95f;

        if (input.TargetCenterX < input.PositionX && velocityX > -5f)
            velocityX -= .1f;
        else if (input.TargetCenterX > right && velocityX < 5f)
            velocityX += .1f;

        result = new VanillaGroundFighter258AirborneResult(velocityX, input.DirectionX);
        return true;
    }

    public static bool TryResolveGroundLeap(
        float positionY,
        float velocityY,
        float targetCenterY,
        bool canHit,
        out float nextVelocityY)
    {
        nextVelocityY = velocityY;
        if (!float.IsFinite(positionY) ||
            !float.IsFinite(velocityY) ||
            !float.IsFinite(targetCenterY) ||
            velocityY != 0f ||
            targetCenterY + 50f >= positionY ||
            !canHit)
        {
            return false;
        }

        nextVelocityY = -7f;
        return true;
    }
}
