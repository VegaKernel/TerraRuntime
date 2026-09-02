using TerraRuntime.Gameplay.Npcs;
namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaServantOfCthulhuMotionInput(
    float NpcCenterX,
    float NpcCenterY,
    float VelocityX,
    float VelocityY,
    float TargetCenterX,
    float TargetCenterY,
    float OldVelocityX = 0f,
    float OldVelocityY = 0f,
    bool CollideX = false,
    bool CollideY = false,
    bool Wet = false);

public readonly record struct VanillaServantOfCthulhuMotionResult(
    float VelocityX,
    float VelocityY);

/// <summary>
/// Clean-room TerrariaServer 1.4.5.8 aiStyle 5 steering slice for Servant of Cthulhu (NPC type 5).
/// The source quantizes both centers to eight-pixel coordinates, seeks at speed 5 and accelerates by 0.03,
/// including the aiStyle-5 double acceleration while crossing an axis through zero.
/// </summary>
public static class VanillaServantOfCthulhuMotion
{
    private const float CoordinateQuantum = 8f;

    public static bool TryStep(
        in VanillaServantOfCthulhuMotionInput input,
        out VanillaServantOfCthulhuMotionResult result) =>
        TryStep(
            in input,
            new VanillaFlyerMotionProfile(5f, 0.03f, true, 0f, 0f, 0f),
            out result);

    public static bool TryStep(
        in VanillaServantOfCthulhuMotionInput input,
        in VanillaFlyerMotionProfile profile,
        out VanillaServantOfCthulhuMotionResult result)
    {
        if (!float.IsFinite(input.NpcCenterX) ||
            !float.IsFinite(input.NpcCenterY) ||
            !float.IsFinite(input.VelocityX) ||
            !float.IsFinite(input.VelocityY) ||
            !float.IsFinite(input.TargetCenterX) ||
            !float.IsFinite(input.TargetCenterY) ||
            !float.IsFinite(input.OldVelocityX) ||
            !float.IsFinite(input.OldVelocityY) ||
            !profile.IsValid)
        {
            result = default;
            return false;
        }

        float sourceX = Quantize(input.NpcCenterX);
        float sourceY = Quantize(input.NpcCenterY);
        float targetX = Quantize(input.TargetCenterX);
        float targetY = Quantize(input.TargetCenterY);
        float deltaX = targetX - sourceX;
        float deltaY = targetY - sourceY;
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);

        float desiredX;
        float desiredY;
        if (distance == 0f)
        {
            desiredX = input.VelocityX;
            desiredY = input.VelocityY;
        }
        else
        {
            float scale = profile.MaximumSpeed / distance;
            desiredX = deltaX * scale;
            desiredY = deltaY * scale;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        ApproachAxis(ref velocityX, desiredX, profile.Acceleration, profile.TurnsHard);
        ApproachAxis(ref velocityY, desiredY, profile.Acceleration, profile.TurnsHard);

        if (profile.HasBounce)
        {
            if (input.CollideX)
                velocityX = input.OldVelocityX * -profile.BounceFactor;
            if (input.CollideY)
                velocityY = input.OldVelocityY * -profile.BounceFactor;
        }

        if (input.Wet && profile.RisesInWater)
        {
            if (velocityY > 0f)
                velocityY *= 0.95f;
            velocityY -= profile.WaterRiseAcceleration;
            if (velocityY < -profile.WaterRiseSpeedCap)
                velocityY = -profile.WaterRiseSpeedCap;
        }

        result = new VanillaServantOfCthulhuMotionResult(velocityX, velocityY);
        return true;
    }

    private static float Quantize(float value) => (int)(value / CoordinateQuantum) * CoordinateQuantum;

    private static void ApproachAxis(
        ref float velocity,
        float desired,
        float acceleration,
        bool turnsHard)
    {
        if (velocity < desired)
        {
            velocity += acceleration;
            if (turnsHard && velocity < 0f && desired > 0f)
                velocity += acceleration;
        }
        else if (velocity > desired)
        {
            velocity -= acceleration;
            if (turnsHard && velocity > 0f && desired < 0f)
                velocity -= acceleration;
        }
    }
}
