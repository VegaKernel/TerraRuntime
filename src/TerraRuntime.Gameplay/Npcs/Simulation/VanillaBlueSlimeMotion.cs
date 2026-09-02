using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaBlueSlimeTargetRefresh(
    bool HasTarget,
    ushort Target,
    int DirectionX,
    int DirectionY)
{
    public bool IsValid =>
        (!HasTarget ||
         (Target < byte.MaxValue && DirectionX is -1 or 1 && DirectionY is -1 or 1)) &&
        DirectionX is >= -1 and <= 1 &&
        DirectionY is >= -1 and <= 1;
}

public readonly record struct VanillaBlueSlimeMotionInput(
    float PositionX,
    float VelocityX,
    float VelocityY,
    float OldVelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    bool Wet,
    bool CollideX,
    bool CollideY,
    bool Engaged,
    bool SolidCollision,
    VanillaBlueSlimeTargetRefresh ClosestTarget,
    float TimerBonus = 0f,
    float JumpTimerBand = -1000f);

public readonly record struct VanillaBlueSlimeMotionResult(
    float PositionX,
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    int TargetRefreshes);

/// <summary>
/// Deterministic state/movement slice of TerrariaServer 1.4.5.8 NPC.AI_001_Slimes for the ordinary
/// Blue Slime (type 1). Item generation, paint/color, dust, projectiles and other side effects are
/// deliberately excluded; this primitive covers the synchronized AI slots, facing, jump cadence,
/// wet escape behavior, ground friction and airborne steering that affect authoritative movement.
/// </summary>
public static class VanillaBlueSlimeMotion
{
    public static bool TryStep(
        in VanillaBlueSlimeMotionInput input,
        out VanillaBlueSlimeMotionResult result)
    {
        if (!float.IsFinite(input.PositionX) ||
            !float.IsFinite(input.VelocityX) ||
            !float.IsFinite(input.VelocityY) ||
            !float.IsFinite(input.OldVelocityY) ||
            input.DirectionX is < -1 or > 1 ||
            input.DirectionY is < -1 or > 1 ||
            input.Target > byte.MaxValue ||
            !input.Ai.IsFinite ||
            !input.ClosestTarget.IsValid ||
            !float.IsFinite(input.TimerBonus) ||
            input.TimerBonus < 0f ||
            !float.IsFinite(input.JumpTimerBand) ||
            input.JumpTimerBand >= 0f)
        {
            result = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closestTarget = input.ClosestTarget;
        float positionX = input.PositionX;
        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        int directionX = input.DirectionX;
        int directionY = input.DirectionY;
        ushort target = input.Target;
        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;
        float ai2 = input.Ai.Ai2;
        float ai3 = input.Ai.Ai3;
        int targetRefreshes = 0;

        // Ordinary Blue Slime has no contained-item hover override. Vanilla initializes a zero
        // horizontal direction to +1 before entering the movement state machine.
        if (directionX == 0)
            directionX = 1;

        if (ai0 == -999f)
        {
            result = CreateResult();
            return true;
        }

        if (ai2 > 1f)
            ai2--;

        if (input.Wet)
        {
            if (input.CollideY)
                velocityY = -2f;

            if (velocityY < 0f && ai3 == positionX)
            {
                directionX *= -1;
                ai2 = 200f;
            }

            if (velocityY > 0f)
                ai3 = positionX;

            if (velocityY > 2f)
                velocityY *= 0.9f;

            velocityY -= 0.5f;
            if (velocityY < -4f)
                velocityY = -4f;

            if (ai2 == 1f && input.Engaged)
                RefreshTarget();
        }

        if (ai2 == 0f)
        {
            ai0 = -100f;
            ai2 = 1f;
            RefreshTarget();
        }

        if (velocityY == 0f)
        {
            if (input.CollideY && input.OldVelocityY != 0f && input.SolidCollision)
                positionX -= velocityX + directionX;

            if (ai3 == positionX)
            {
                directionX *= -1;
                ai2 = 200f;
            }

            ai3 = 0f;
            if (ai1 == 3609f)
            {
                velocityX += directionX < 0 ? -0.1f : 0.1f;
                velocityX = Math.Clamp(velocityX, -2.5f, 2.5f);
            }
            else
            {
                velocityX *= 0.8f;
                if (velocityX > -0.1f && velocityX < 0.1f)
                    velocityX = 0f;
            }

            ai0 += input.TimerBonus;
            if (input.Engaged)
                ai0++;
            ai0++;

            int jumpKind = 0;
            if (ai0 >= 0f)
                jumpKind = 1;
            if (ai0 >= input.JumpTimerBand && ai0 <= input.JumpTimerBand * 0.5f)
                jumpKind = 2;
            if (ai0 >= input.JumpTimerBand * 2f && ai0 <= input.JumpTimerBand * 1.5f)
                jumpKind = 3;

            if (jumpKind > 0)
            {
                if (input.Engaged && ai2 == 1f)
                    RefreshTarget();

                if (jumpKind == 3)
                {
                    velocityY = -8f;
                    velocityX += 3f * directionX;
                    ai0 = -200f;
                    ai3 = positionX;
                }
                else
                {
                    velocityY = -6f;
                    velocityX += 2f * directionX;
                    ai0 = -120f;
                    ai0 += jumpKind == 1
                        ? input.JumpTimerBand
                        : input.JumpTimerBand * 2f;
                }
            }
        }
        else if (target < byte.MaxValue &&
                 ((directionX == 1 && velocityX < 3f) ||
                  (directionX == -1 && velocityX > -3f)))
        {
            if (input.CollideX && Math.Abs(velocityX) == 0.2f)
                positionX -= 1.4f * directionX;

            if (input.CollideY && input.OldVelocityY != 0f && input.SolidCollision)
                positionX -= velocityX + directionX;

            if ((directionX == -1 && velocityX < 0.01f) ||
                (directionX == 1 && velocityX > -0.01f))
            {
                velocityX += 0.2f * directionX;
            }
            else
            {
                velocityX *= 0.93f;
            }
        }

        result = CreateResult();
        return true;

        void RefreshTarget()
        {
            targetRefreshes++;
            if (!closestTarget.HasTarget)
                return;

            target = closestTarget.Target;
            directionX = closestTarget.DirectionX;
            directionY = closestTarget.DirectionY;
        }

        VanillaBlueSlimeMotionResult CreateResult() =>
            new(
                positionX,
                velocityX,
                velocityY,
                directionX,
                directionY,
                target,
                new NpcAiState(ai0, ai1, ai2, ai3),
                targetRefreshes);
    }
}
