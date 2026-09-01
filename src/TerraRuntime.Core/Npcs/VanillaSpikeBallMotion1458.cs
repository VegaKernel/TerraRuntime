using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaSpikeBallMotionInput1458(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    int Width,
    int Height,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    VanillaBlueSlimeTargetRefresh ClosestTarget)
{
    public bool IsValid =>
        float.IsFinite(PositionX) && float.IsFinite(PositionY) &&
        float.IsFinite(VelocityX) && float.IsFinite(VelocityY) &&
        Width > 0 && Height > 0 &&
        DirectionX is >= -1 and <= 1 && DirectionY is >= -1 and <= 1 &&
        Ai.IsFinite && ClosestTarget.IsValid;
}

public readonly record struct VanillaSpikeBallMotionResult1458(
    float PositionY,
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai);

/// <summary>Complete server-relevant TerrariaServer 1.4.5.8 aiStyle 20 state/motion primitive.</summary>
public static class VanillaSpikeBallMotion1458
{
    public static bool TryStep(
        in VanillaSpikeBallMotionInput1458 input,
        IVanillaNpcRandom random,
        out VanillaSpikeBallMotionResult1458 result)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!input.IsValid)
        {
            result = default;
            return false;
        }

        float positionY = input.PositionY;
        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        int directionX = input.DirectionX;
        int directionY = input.DirectionY;
        ushort target = input.Target;
        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;
        float ai2 = input.Ai.Ai2;
        float ai3 = input.Ai.Ai3;

        if (ai0 == 0f)
        {
            if (input.ClosestTarget.HasTarget)
            {
                target = input.ClosestTarget.Target;
                directionX = input.ClosestTarget.DirectionX;
                directionY = input.ClosestTarget.DirectionY;
            }

            directionX *= -1;
            directionY *= -1;
            positionY += input.Height / 2 + 8;
            ai1 = input.PositionX + input.Width * 0.5f;
            ai2 = positionY + input.Height * 0.5f;
            if (directionX == 0)
                directionX = 1;
            if (directionY == 0)
                directionY = 1;
            ai3 = 1f + random.NextInt32(0, 15) * 0.1f;
            velocityY = directionY * 6f * ai3;
            ai0 += 1f;
            result = Build();
            return true;
        }

        float maximumSpeed = 6f * ai3;
        float acceleration = 0.2f * ai3;
        float transitionTicks = maximumSpeed / acceleration / 2f;
        if (ai0 >= 1f && ai0 < (int)transitionTicks)
        {
            velocityY = directionY * maximumSpeed;
            ai0 += 1f;
            result = Build();
            return true;
        }

        if (ai0 >= (int)transitionTicks)
        {
            velocityY = 0f;
            directionY *= -1;
            velocityX = maximumSpeed * directionX;
            ai0 = -1f;
            result = Build();
            return true;
        }

        if (directionY > 0)
        {
            if (velocityY >= maximumSpeed)
            {
                directionY *= -1;
                velocityY = maximumSpeed;
            }
        }
        else if (directionY < 0 && velocityY <= -maximumSpeed)
        {
            directionY *= -1;
            velocityY = -maximumSpeed;
        }

        if (directionX > 0)
        {
            if (velocityX >= maximumSpeed)
            {
                directionX *= -1;
                velocityX = maximumSpeed;
            }
        }
        else if (directionX < 0 && velocityX <= -maximumSpeed)
        {
            directionX *= -1;
            velocityX = -maximumSpeed;
        }

        velocityX += acceleration * directionX;
        velocityY += acceleration * directionY;
        result = Build();
        return true;

        VanillaSpikeBallMotionResult1458 Build() => new(
            positionY,
            velocityX,
            velocityY,
            directionX,
            directionY,
            target,
            new NpcAiState(ai0, ai1, ai2, ai3));
    }
}
