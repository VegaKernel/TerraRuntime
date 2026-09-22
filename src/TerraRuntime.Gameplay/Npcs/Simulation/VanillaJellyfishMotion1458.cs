using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Input to the server-relevant TerrariaServer 1.4.5.8 AI_018 Jellyfish motion slice.</summary>
public readonly record struct VanillaJellyfishMotionInput1458(
    float VelocityX,
    float VelocityY,
    float PositionX,
    float PositionY,
    int Width,
    int Height,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    bool Wet,
    bool CollideX,
    bool CollideY,
    bool Friendly,
    VanillaFishBottomSlope1458 BottomSlope,
    bool DeepLiquidAboveAndActiveTileBelow,
    VanillaBlueSlimeTargetRefresh Closest,
    bool ClosestWetAndVisible,
    float ClosestCenterX,
    float ClosestCenterY);

/// <summary>Authoritative state updated by the AI_018 Jellyfish motion slice.</summary>
public readonly record struct VanillaJellyfishMotionResult1458(
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai);

/// <summary>
/// Server-relevant TerrariaServer 1.4.5.8 AI_018 hostile Jellyfish movement. Lighting, rotation and localAI[2]
/// feed the source animation path only, so they deliberately do not enter this authoritative state transition.
/// </summary>
public static class VanillaJellyfishMotion1458
{
    public static bool TryStep(
        in VanillaJellyfishMotionInput1458 input,
        out VanillaJellyfishMotionResult1458 result)
    {
        if (!IsValid(in input))
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        int directionX = input.DirectionX;
        int directionY = input.DirectionY;
        ushort target = input.Target;
        NpcAiState ai = input.Ai;

        if (directionX == 0 && input.Closest.HasTarget)
        {
            target = input.Closest.Target;
            directionX = input.Closest.DirectionX;
            directionY = input.Closest.DirectionY;
        }

        if (!input.Wet)
        {
            if (velocityY == 0f)
            {
                velocityX *= .98f;
                if (velocityX is > -.01f and < .01f)
                    velocityX = 0f;
            }

            velocityY += .2f;
            if (velocityY > 10f)
                velocityY = 10f;
            result = new(velocityX, velocityY, directionX, directionY, target, ai with { Ai0 = 1f });
            return true;
        }

        ApplyBottomSlope(input.BottomSlope, ref velocityX, ref directionX);
        if (input.CollideX)
        {
            velocityX *= -1f;
            directionX *= -1;
        }

        if (input.CollideY)
        {
            if (velocityY > 0f)
            {
                velocityY = -MathF.Abs(velocityY);
                directionY = -1;
                ai = ai with { Ai0 = -1f };
            }
            else if (velocityY < 0f)
            {
                velocityY = MathF.Abs(velocityY);
                directionY = 1;
                ai = ai with { Ai0 = 1f };
            }
        }

        if (!input.Friendly && input.ClosestWetAndVisible)
        {
            target = input.Closest.Target;
            velocityX *= .98f;
            velocityY *= .98f;
            if (velocityX is > -.2f and < .2f && velocityY is > -.2f and < .2f)
            {
                float centerX = input.PositionX + input.Width * .5f;
                float centerY = input.PositionY + input.Height * .5f;
                float targetDeltaX = input.ClosestCenterX - centerX;
                float targetDeltaY = input.ClosestCenterY - centerY;
                float targetDistance = MathF.Sqrt(targetDeltaX * targetDeltaX + targetDeltaY * targetDeltaY);
                if (!(targetDistance > 0f) || !float.IsFinite(targetDistance))
                {
                    result = default;
                    return false;
                }

                velocityX = targetDeltaX / targetDistance * 7f;
                velocityY = targetDeltaY / targetDistance * 7f;
            }

            result = new(velocityX, velocityY, directionX, directionY, target, ai);
            return true;
        }

        velocityX += directionX * .02f;
        if (velocityX is < -1f or > 1f)
            velocityX *= .95f;
        if (ai.Ai0 == -1f)
        {
            velocityY -= .01f;
            if (velocityY < -1f)
                ai = ai with { Ai0 = 1f };
        }
        else
        {
            velocityY += .01f;
            if (velocityY > 1f)
                ai = ai with { Ai0 = -1f };
        }

        ai = ai with { Ai0 = input.DeepLiquidAboveAndActiveTileBelow ? -1f : 1f };
        if (velocityY is < -1.2f or > 1.2f)
            velocityY *= .99f;
        result = new(velocityX, velocityY, directionX, directionY, target, ai);
        return true;
    }

    private static void ApplyBottomSlope(
        VanillaFishBottomSlope1458 slope,
        ref float velocityX,
        ref int directionX)
    {
        if (slope == VanillaFishBottomSlope1458.FaceLeft)
        {
            directionX = -1;
            velocityX = -MathF.Abs(velocityX);
        }
        else if (slope == VanillaFishBottomSlope1458.FaceRight)
        {
            directionX = 1;
            velocityX = MathF.Abs(velocityX);
        }
    }

    private static bool IsValid(in VanillaJellyfishMotionInput1458 input) =>
        float.IsFinite(input.VelocityX) &&
        float.IsFinite(input.VelocityY) &&
        float.IsFinite(input.PositionX) &&
        float.IsFinite(input.PositionY) &&
        float.IsFinite(input.ClosestCenterX) &&
        float.IsFinite(input.ClosestCenterY) &&
        input.Width > 0 &&
        input.Height > 0 &&
        input.DirectionX is >= -1 and <= 1 &&
        input.DirectionY is >= -1 and <= 1 &&
        input.Closest.DirectionX is >= -1 and <= 1 &&
        input.Closest.DirectionY is >= -1 and <= 1;
}
