using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaBatMotionInput1458(
    float VelocityX,
    float VelocityY,
    float OldVelocityX,
    float OldVelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    bool Wet,
    bool CollideX,
    bool CollideY,
    VanillaBlueSlimeTargetRefresh ClosestTarget,
    bool TargetDryAndVisible)
{
    public bool IsValid =>
        float.IsFinite(VelocityX) && float.IsFinite(VelocityY) &&
        float.IsFinite(OldVelocityX) && float.IsFinite(OldVelocityY) &&
        DirectionX is >= -1 and <= 1 && DirectionY is >= -1 and <= 1 &&
        Ai.IsFinite && ClosestTarget.IsValid;
}

public readonly record struct VanillaBatMotionResult1458(
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai);

/// <summary>
/// Server-relevant TerrariaServer 1.4.5.8 aiStyle 14 collision rebound, pursuit, wet escape and wander clock for
/// the admitted ordinary bat/Slimer/Queen Slime minion roster. Shooters and Vampire Bat remain separate slices.
/// </summary>
public static class VanillaBatMotion1458
{
    public static bool TryStep(
        NpcTypeId type,
        in VanillaBatMotionInput1458 input,
        out VanillaBatMotionResult1458 result)
    {
        if (!input.IsValid || !VanillaBatNpcCatalog1458.IsSupportedMotionType(type))
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        int directionX = input.DirectionX;
        int directionY = input.DirectionY;
        ushort target = input.Target;
        float ai1 = input.Ai.Ai1;
        float ai2 = input.Ai.Ai2;

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
        if (type == VanillaNpcIds.QueenSlimeMinionPurple)
            ApplyQueenSlimeMinionAcceleration(ref velocityX, ref velocityY, directionX, directionY);
        else
            ApplyOrdinaryAcceleration(ref velocityX, ref velocityY, directionX, directionY);

        bool doubleAcceleration = VanillaBatNpcCatalog1458.UsesBatDoubleAcceleration(type);
        if (doubleAcceleration && input.Wet)
        {
            if (velocityY > 0f)
                velocityY *= 0.95f;
            velocityY -= 0.5f;
            if (velocityY < -4f)
                velocityY = -4f;
            ApplyClosest(input.ClosestTarget, ref target, ref directionX, ref directionY);
        }

        if (doubleAcceleration && type == VanillaNpcIds.Hellbat)
        {
            AccelerateAxis(ref velocityX, directionX, 0.1f, 4f, 0.07f, 0.03f);
            AccelerateAxis(ref velocityY, directionY, 0.04f, 1.5f, 0.03f, 0.02f);
        }
        else if (doubleAcceleration)
        {
            ApplyOrdinaryAcceleration(ref velocityX, ref velocityY, directionX, directionY);
        }

        ai1++;
        if (ai1 > 200f)
        {
            if (input.TargetDryAndVisible)
                ai1 = 0f;

            if (ai1 > 1000f)
                ai1 = 0f;

            ai2++;
            if (ai2 > 0f)
            {
                if (velocityY < 1.5f)
                    velocityY += 0.1f;
            }
            else if (velocityY > -1.5f)
            {
                velocityY -= 0.1f;
            }

            if (ai2 < -150f || ai2 > 150f)
            {
                if (velocityX < 4f)
                    velocityX += 0.2f;
            }
            else if (velocityX > -4f)
            {
                velocityX -= 0.2f;
            }

            if (ai2 > 300f)
                ai2 = -300f;
        }

        result = new VanillaBatMotionResult1458(
            velocityX,
            velocityY,
            directionX,
            directionY,
            target,
            new NpcAiState(input.Ai.Ai0, ai1, ai2, input.Ai.Ai3));
        return true;
    }

    private static void ApplyOrdinaryAcceleration(
        ref float velocityX,
        ref float velocityY,
        int directionX,
        int directionY)
    {
        AccelerateAxis(ref velocityX, directionX, 0.1f, 4f, 0.1f, 0.05f);
        AccelerateAxis(ref velocityY, directionY, 0.04f, 1.5f, 0.05f, 0.03f);
    }

    private static void ApplyQueenSlimeMinionAcceleration(
        ref float velocityX,
        ref float velocityY,
        int directionX,
        int directionY)
    {
        AccelerateAxis(ref velocityX, directionX, 0.35f, 6f, 0.35f, 0.175f);
        AccelerateAxis(ref velocityY, directionY, 0.3f, 5f, 0.3f, 0.225f);
    }

    private static void AccelerateAxis(
        ref float velocity,
        int direction,
        float acceleration,
        float maximumSpeed,
        float overspeedCorrection,
        float opposingCorrection)
    {
        if (direction == -1 && velocity > -maximumSpeed)
        {
            velocity -= acceleration;
            if (velocity > maximumSpeed)
                velocity -= overspeedCorrection;
            else if (velocity > 0f)
                velocity += opposingCorrection;
            if (velocity < -maximumSpeed)
                velocity = -maximumSpeed;
        }
        else if (direction == 1 && velocity < maximumSpeed)
        {
            velocity += acceleration;
            if (velocity < -maximumSpeed)
                velocity += overspeedCorrection;
            else if (velocity < 0f)
                velocity -= opposingCorrection;
            if (velocity > maximumSpeed)
                velocity = maximumSpeed;
        }
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
