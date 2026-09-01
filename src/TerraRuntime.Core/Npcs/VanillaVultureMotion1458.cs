using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaVultureMotionInput1458(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    float OldVelocityX,
    float OldVelocityY,
    int Width,
    int Height,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    bool Wet,
    bool CollideX,
    bool CollideY,
    int Life,
    int LifeMax,
    bool CurrentTargetDead,
    VanillaBlueSlimeTargetRefresh ClosestTarget,
    float ClosestTargetCenterX,
    float ClosestTargetCenterY)
{
    public bool IsValid =>
        float.IsFinite(PositionX) && float.IsFinite(PositionY) &&
        float.IsFinite(VelocityX) && float.IsFinite(VelocityY) &&
        float.IsFinite(OldVelocityX) && float.IsFinite(OldVelocityY) &&
        Width > 0 && Height > 0 &&
        DirectionX is >= -1 and <= 1 && DirectionY is >= -1 and <= 1 &&
        Ai.IsFinite && ClosestTarget.IsValid &&
        LifeMax > 0 && Life >= 0 && Life <= LifeMax &&
        (!ClosestTarget.HasTarget ||
         (float.IsFinite(ClosestTargetCenterX) && float.IsFinite(ClosestTargetCenterY)));
}

public readonly record struct VanillaVultureMotionResult1458(
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    bool NoGravity);

/// <summary>Server-relevant TerrariaServer 1.4.5.8 aiStyle 17 motion for Vulture and Raven.</summary>
public static class VanillaVultureMotion1458
{
    public static bool TryStep(
        in VanillaVultureMotionInput1458 input,
        out VanillaVultureMotionResult1458 result)
    {
        if (!input.IsValid)
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        int directionX = input.DirectionX;
        int directionY = input.DirectionY;
        ushort target = input.Target;
        float ai0 = input.Ai.Ai0;
        bool noGravity = true;

        if (ai0 == 0f)
        {
            noGravity = false;
            ApplyClosest(input.ClosestTarget, ref target, ref directionX, ref directionY);

            if (velocityX != 0f || velocityY < 0f || velocityY > 0.3f)
            {
                ai0 = 1f;
            }
            else if ((input.ClosestTarget.HasTarget && TargetIntersectsActivationArea(in input)) ||
                     input.Life < input.LifeMax)
            {
                ai0 = 1f;
                velocityY -= 6f;
            }
        }
        else if (!input.CurrentTargetDead)
        {
            if (input.CollideX)
            {
                velocityX = input.OldVelocityX * -0.5f;
                if (directionX == -1 && velocityX > 0f && velocityX < 2f)
                    velocityX = 2f;
                if (directionX == 1 && velocityX < 0f && velocityX > -2f)
                    velocityX = -2f;
            }

            if (input.CollideY)
            {
                velocityY = input.OldVelocityY * -0.5f;
                if (velocityY > 0f && velocityY < 1f)
                    velocityY = 1f;
                if (velocityY < 0f && velocityY > -1f)
                    velocityY = -1f;
            }

            ApplyClosest(input.ClosestTarget, ref target, ref directionX, ref directionY);
            if (directionX == -1 && velocityX > -3f)
            {
                velocityX -= 0.1f;
                if (velocityX > 3f)
                    velocityX -= 0.1f;
                else if (velocityX > 0f)
                    velocityX -= 0.05f;
                if (velocityX < -3f)
                    velocityX = -3f;
            }
            else if (directionX == 1 && velocityX < 3f)
            {
                velocityX += 0.1f;
                if (velocityX < -3f)
                    velocityX += 0.1f;
                else if (velocityX < 0f)
                    velocityX += 0.05f;
                if (velocityX > 3f)
                    velocityX = 3f;
            }

            if (input.ClosestTarget.HasTarget)
            {
                float horizontalDistance = MathF.Abs(
                    input.PositionX + input.Width * 0.5f - input.ClosestTargetCenterX);
                float targetY = input.ClosestTargetCenterY -
                                VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f -
                                input.Height * 0.5f;
                if (horizontalDistance > 50f)
                    targetY -= 100f;

                if (input.PositionY < targetY)
                {
                    velocityY += 0.05f;
                    if (velocityY < 0f)
                        velocityY += 0.01f;
                }
                else
                {
                    velocityY -= 0.05f;
                    if (velocityY > 0f)
                        velocityY -= 0.01f;
                }

                velocityY = Math.Clamp(velocityY, -3f, 3f);
            }
        }

        if (input.Wet)
        {
            if (velocityY > 0f)
                velocityY *= 0.95f;
            velocityY -= 0.5f;
            if (velocityY < -4f)
                velocityY = -4f;
            ApplyClosest(input.ClosestTarget, ref target, ref directionX, ref directionY);
        }

        result = new VanillaVultureMotionResult1458(
            velocityX,
            velocityY,
            directionX,
            directionY,
            target,
            new NpcAiState(ai0, input.Ai.Ai1, input.Ai.Ai2, input.Ai.Ai3),
            noGravity);
        return true;
    }

    private static bool TargetIntersectsActivationArea(in VanillaVultureMotionInput1458 input)
    {
        float playerLeft = input.ClosestTargetCenterX - VanillaNpcBehaviorContext.BasePlayerWidth * 0.5f;
        float playerTop = input.ClosestTargetCenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f;
        float left = input.PositionX - 100f;
        float top = input.PositionY - 100f;
        float width = input.Width + 200f;
        float height = input.Height + 200f;
        return left < playerLeft + VanillaNpcBehaviorContext.BasePlayerWidth &&
               left + width > playerLeft &&
               top < playerTop + VanillaNpcBehaviorContext.BasePlayerHeight &&
               top + height > playerTop;
    }

    private static void ApplyClosest(
        VanillaBlueSlimeTargetRefresh closest,
        ref ushort target,
        ref int directionX,
        ref int directionY)
    {
        if (!closest.HasTarget)
            return;

        target = closest.Target;
        directionX = closest.DirectionX;
        directionY = closest.DirectionY;
    }
}
