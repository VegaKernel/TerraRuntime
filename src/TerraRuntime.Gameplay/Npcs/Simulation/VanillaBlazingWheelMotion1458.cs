using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaBlazingWheelMotionInput1458(
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    bool CollideX,
    bool CollideY,
    VanillaBlueSlimeTargetRefresh ClosestTarget)
{
    public bool IsValid =>
        float.IsFinite(VelocityX) && float.IsFinite(VelocityY) &&
        DirectionX is >= -1 and <= 1 && DirectionY is >= -1 and <= 1 &&
        Ai.IsFinite && ClosestTarget.IsValid;
}

public readonly record struct VanillaBlazingWheelMotionResult1458(
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai);

/// <summary>Complete server-relevant TerrariaServer 1.4.5.8 aiStyle 21 wall-following state machine.</summary>
public static class VanillaBlazingWheelMotion1458
{
    public static bool TryStep(
        in VanillaBlazingWheelMotionInput1458 input,
        out VanillaBlazingWheelMotionResult1458 result)
    {
        if (!input.IsValid)
        {
            result = default;
            return false;
        }

        int directionX = input.DirectionX;
        int directionY = input.DirectionY;
        ushort target = input.Target;
        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;

        if (ai0 == 0f)
        {
            if (input.ClosestTarget.HasTarget)
            {
                target = input.ClosestTarget.Target;
                directionX = input.ClosestTarget.DirectionX;
            }
            directionY = 1;
            ai0 = 1f;
        }

        if (ai1 == 0f)
        {
            if (input.CollideY)
                ai0 = 2f;
            if (!input.CollideY && ai0 == 2f)
            {
                directionX = -directionX;
                ai1 = 1f;
                ai0 = 1f;
            }
            if (input.CollideX)
            {
                directionY = -directionY;
                ai1 = 1f;
            }
        }
        else
        {
            if (input.CollideX)
                ai0 = 2f;
            if (!input.CollideX && ai0 == 2f)
            {
                directionY = -directionY;
                ai1 = 0f;
                ai0 = 1f;
            }
            if (input.CollideY)
            {
                directionX = -directionX;
                ai1 = 0f;
            }
        }

        result = new VanillaBlazingWheelMotionResult1458(
            6f * directionX,
            6f * directionY,
            directionX,
            directionY,
            target,
            new NpcAiState(ai0, ai1, input.Ai.Ai2, input.Ai.Ai3));
        return true;
    }
}
