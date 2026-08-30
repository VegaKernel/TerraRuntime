namespace TerraRuntime.Core;

public readonly record struct VanillaServantOfCthulhuMotionInput(
    float NpcCenterX,
    float NpcCenterY,
    float VelocityX,
    float VelocityY,
    float TargetCenterX,
    float TargetCenterY);

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
    private const float Speed = 5f;
    private const float Acceleration = 0.03f;
    private const float CoordinateQuantum = 8f;

    public static bool TryStep(
        in VanillaServantOfCthulhuMotionInput input,
        out VanillaServantOfCthulhuMotionResult result)
    {
        if (!float.IsFinite(input.NpcCenterX) ||
            !float.IsFinite(input.NpcCenterY) ||
            !float.IsFinite(input.VelocityX) ||
            !float.IsFinite(input.VelocityY) ||
            !float.IsFinite(input.TargetCenterX) ||
            !float.IsFinite(input.TargetCenterY))
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
            float scale = Speed / distance;
            desiredX = deltaX * scale;
            desiredY = deltaY * scale;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        ApproachAxis(ref velocityX, desiredX);
        ApproachAxis(ref velocityY, desiredY);
        result = new VanillaServantOfCthulhuMotionResult(velocityX, velocityY);
        return true;
    }

    private static float Quantize(float value) => (int)(value / CoordinateQuantum) * CoordinateQuantum;

    private static void ApproachAxis(ref float velocity, float desired)
    {
        if (velocity < desired)
        {
            velocity += Acceleration;
            if (velocity < 0f && desired > 0f)
                velocity += Acceleration;
        }
        else if (velocity > desired)
        {
            velocity -= Acceleration;
            if (velocity > 0f && desired < 0f)
                velocity -= Acceleration;
        }
    }
}
