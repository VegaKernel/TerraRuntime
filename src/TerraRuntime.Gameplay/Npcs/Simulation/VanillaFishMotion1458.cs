using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaFishMotionInput1458(
    float VelocityX,
    float VelocityY,
    float PositionY,
    float CenterY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    NpcAiState LocalAi,
    bool JustHit,
    bool Wet,
    bool CollideX,
    bool CollideY,
    VanillaFishBottomSlope1458 BottomSlope,
    bool DeepLiquidAboveAndActiveTileBelow,
    VanillaBlueSlimeTargetRefresh ClosestTarget,
    bool PursuingWetTarget,
    bool TargetTopBelowNpcTop,
    bool HasGroundedLaunch,
    float GroundedLaunchVelocityX,
    float GroundedLaunchVelocityY,
    int GroundedLaunchDirection,
    int DolphinWaitThreshold,
    int DolphinNextState,
    bool DolphinCanHitLineAbove,
    bool HasWaterLine,
    float WaterLineHeight)
{
    public bool IsValid =>
        float.IsFinite(VelocityX) &&
        float.IsFinite(VelocityY) &&
        float.IsFinite(PositionY) &&
        float.IsFinite(CenterY) &&
        DirectionX is >= -1 and <= 1 &&
        DirectionY is >= -1 and <= 1 &&
        Ai.IsFinite &&
        LocalAi.IsFinite &&
        Enum.IsDefined(BottomSlope) &&
        ClosestTarget.IsValid &&
        (!HasGroundedLaunch ||
         (float.IsFinite(GroundedLaunchVelocityX) &&
          float.IsFinite(GroundedLaunchVelocityY) &&
          GroundedLaunchDirection is -1 or 1)) &&
        DolphinWaitThreshold is >= 300 and < 1200 &&
        DolphinNextState is 1 or 2 &&
        (!HasWaterLine || float.IsFinite(WaterLineHeight));
}

public readonly record struct VanillaFishMotionResult1458(
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    NpcAiState LocalAi);

/// <summary>
/// Server-relevant TerrariaServer 1.4.5.8 AI_016 ordinary swimming, wet-target pursuit, collision response,
/// idle depth steering and dry flopping. Rotation and lighting are presentation-only and intentionally omitted.
/// </summary>
public static class VanillaFishMotion1458
{
    public static bool TryStep(
        NpcTypeId type,
        in VanillaFishMotionInput1458 input,
        out VanillaFishMotionResult1458 result)
    {
        if (!input.IsValid || !VanillaFishNpcCatalog1458.TryGetDefinition(type, out _))
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
        float ai1 = input.Ai.Ai1;
        float ai2 = input.Ai.Ai2;
        float ai3 = input.Ai.Ai3;
        float localAi0 = input.LocalAi.Ai0;

        if (directionX == 0)
            ApplyClosest(input.ClosestTarget, faceTarget: true, ref target, ref directionX, ref directionY);

        if (type == VanillaNpcIds.Pufferfish)
        {
            if (input.JustHit && ai2 == 0f)
            {
                ai2 = 1f;
                localAi0 = 180f;
            }
            else
            {
                localAi0--;
                if (localAi0 <= 0f)
                {
                    localAi0 = 120f;
                    if (ai2 == 1f)
                        ai2 = 0f;
                    if (input.JustHit)
                        ai2 = 1f;
                }
            }

            if (ai2 == 1f)
            {
                velocityX *= 0.98f;
                velocityY *= 0.98f;
                ApplyPufferfishVertical(
                    input.Wet,
                    input.HasWaterLine,
                    ref velocityY);
                if (input.HasWaterLine)
                {
                    ApplyPufferfishWaterLine(
                        input.WaterLineHeight,
                        input.PositionY - 5f,
                        input.CenterY,
                        ref velocityY);
                }
                result = CreateResult(
                    velocityX, velocityY, directionX, directionY, target,
                    ai0, ai1, ai2, ai3, in input, localAi0);
                return true;
            }
        }

        if (type == VanillaNpcIds.Dolphin)
        {
            if (ai2 == 0f && ++ai3 >= input.DolphinWaitThreshold)
            {
                ai2 = input.DolphinNextState;
                if (ai2 == 1f && !input.DolphinCanHitLineAbove)
                    ai2 = 2f;
                if (ai2 == 2f)
                    ApplyClosest(input.ClosestTarget, faceTarget: true, ref target, ref directionX, ref directionY);
                ai3 = 0f;
            }

            if (ai2 == 1f)
            {
                if (input.CollideX || input.CollideY)
                {
                    ai2 = 0f;
                    ai3 = 0f;
                }
                else if (input.Wet)
                {
                    velocityY -= 0.4f;
                    if (velocityY < -6f)
                        velocityY = -6f;
                    if (ai3 == 1f)
                    {
                        ai2 = 0f;
                        ai3 = 0f;
                    }
                }
                else
                {
                    ai3 = 1f;
                    velocityY += 0.3f;
                    if (velocityY > 10f)
                        velocityY = 10f;
                }

                result = CreateResult(
                    velocityX, velocityY, directionX, directionY, target,
                    ai0, ai1, ai2, ai3, in input, localAi0);
                return true;
            }

            if (ai2 == 2f)
            {
                if (input.CollideX || input.CollideY)
                {
                    ai2 = 0f;
                    ai3 = 0f;
                }
                else if (input.Wet)
                {
                    velocityY -= 0.4f;
                    if (velocityY < -6f)
                        velocityY = -6f;
                    if (input.HasWaterLine)
                    {
                        velocityY = Math.Clamp(input.WaterLineHeight - input.PositionY, -2f, 0.5f);
                        velocityX *= 0.95f;
                        ai3++;
                        if (ai3 >= 300f)
                        {
                            ai2 = 0f;
                            ai3 = 0f;
                            velocityY = 4f;
                        }
                    }
                }
                else
                {
                    ai2 = 0f;
                    ai3 = 0f;
                    velocityY += 0.3f;
                    if (velocityY > 10f)
                        velocityY = 10f;
                }

                result = CreateResult(
                    velocityX, velocityY, directionX, directionY, target,
                    ai0, ai1, ai2, ai3, in input, localAi0);
                return true;
            }
        }

        if (input.Wet)
        {
            bool passive = VanillaFishNpcCatalog1458.IsPassiveFish(type);
            if (!passive)
                ApplyClosest(input.ClosestTarget, faceTarget: false, ref target, ref directionX, ref directionY);

            ApplyBottomSlope(input.BottomSlope, ref directionX, ref velocityX);
            if (!input.PursuingWetTarget)
                ApplyCollision(input.CollideX, input.CollideY, ref velocityX, ref velocityY, ref directionX, ref directionY, ref ai0);

            if (input.PursuingWetTarget)
            {
                if (ai0 != 0f)
                    ai0 = 0f;
                ApplyClosest(input.ClosestTarget, faceTarget: true, ref target, ref directionX, ref directionY);
                ApplyPursuit(type, ref velocityX, ref velocityY, directionX, directionY);
            }
            else
            {
                if (ai0 == 0f)
                    ai0 = 1f;

                if (VanillaFishNpcCatalog1458.UsesFastPursuit(type))
                {
                    directionY = input.TargetTopBelowNpcTop ? 1 : -1;
                    ApplyArapaimaIdle(ref velocityX, ref velocityY, directionX, directionY, ref ai0);
                }
                else
                {
                    ApplyOrdinaryIdle(type, ref velocityX, ref velocityY, directionX, ref ai0);
                }

                if (input.DeepLiquidAboveAndActiveTileBelow)
                    ai0 = -1f;
                if (!VanillaFishNpcCatalog1458.UsesFastPursuit(type) && MathF.Abs(velocityY) > 0.4f)
                    velocityY *= 0.95f;
            }
        }
        else
        {
            if (velocityY == 0f)
            {
                if (VanillaFishNpcCatalog1458.UsesGroundedHorizontalDamping(type))
                {
                    velocityX *= 0.94f;
                    if (velocityX is > -0.2f and < 0.2f)
                        velocityX = 0f;
                }
                else if (input.HasGroundedLaunch)
                {
                    velocityY = input.GroundedLaunchVelocityY;
                    velocityX = input.GroundedLaunchVelocityX;
                    directionX = input.GroundedLaunchDirection;
                }
            }

            velocityY += 0.3f;
            if (velocityY > 10f)
                velocityY = 10f;
            ai0 = 1f;
        }

        result = new VanillaFishMotionResult1458(
            velocityX,
            velocityY,
            directionX,
            directionY,
            target,
            new NpcAiState(ai0, ai1, ai2, ai3),
            new NpcAiState(localAi0, input.LocalAi.Ai1, input.LocalAi.Ai2, input.LocalAi.Ai3));
        return true;
    }

    private static VanillaFishMotionResult1458 CreateResult(
        float velocityX,
        float velocityY,
        int directionX,
        int directionY,
        ushort target,
        float ai0,
        float ai1,
        float ai2,
        float ai3,
        in VanillaFishMotionInput1458 input,
        float localAi0) =>
        new(
            velocityX,
            velocityY,
            directionX,
            directionY,
            target,
            new NpcAiState(ai0, ai1, ai2, ai3),
            new NpcAiState(localAi0, input.LocalAi.Ai1, input.LocalAi.Ai2, input.LocalAi.Ai3));

    private static void ApplyPufferfishVertical(
        bool wet,
        bool hasWaterLine,
        ref float velocityY)
    {
        if (hasWaterLine)
            return;
        if (wet)
        {
            velocityY -= 0.3f;
            if (velocityY < -10f)
                velocityY = -10f;
        }
        else
        {
            velocityY += 0.3f;
            if (velocityY > 10f)
                velocityY = 10f;
        }
    }

    private static void ApplyPufferfishWaterLine(
        float waterLineHeight,
        float topMinusFive,
        float centerY,
        ref float velocityY)
    {
        if (centerY > waterLineHeight)
        {
            velocityY -= 0.4f;
            if (velocityY < -2f)
                velocityY = -2f;
            if (topMinusFive + velocityY < waterLineHeight)
                velocityY = waterLineHeight - topMinusFive;
        }
        else
        {
            velocityY = MathF.Min(velocityY, waterLineHeight - topMinusFive);
            if (MathF.Abs(topMinusFive - waterLineHeight) < 2f)
                velocityY = 0f;
        }
    }

    private static void ApplyBottomSlope(
        VanillaFishBottomSlope1458 slope,
        ref int directionX,
        ref float velocityX)
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

    private static void ApplyCollision(
        bool collideX,
        bool collideY,
        ref float velocityX,
        ref float velocityY,
        ref int directionX,
        ref int directionY,
        ref float ai0)
    {
        if (collideX)
        {
            velocityX *= -1f;
            directionX *= -1;
        }

        if (!collideY)
            return;

        if (velocityY > 0f)
        {
            velocityY = -MathF.Abs(velocityY);
            directionY = -1;
            ai0 = -1f;
        }
        else if (velocityY < 0f)
        {
            velocityY = MathF.Abs(velocityY);
            directionY = 1;
            ai0 = 1f;
        }
    }

    private static void ApplyPursuit(
        NpcTypeId type,
        ref float velocityX,
        ref float velocityY,
        int directionX,
        int directionY)
    {
        if (VanillaFishNpcCatalog1458.UsesFastPursuit(type))
        {
            if (velocityX > 0f && directionX < 0 || velocityX < 0f && directionX > 0)
                velocityX *= 0.95f;
            velocityX += directionX * 0.25f;
            velocityY += directionY * 0.2f;
            if (velocityX > 8f) velocityX = 7f;
            if (velocityX < -8f) velocityX = -7f;
            if (velocityY > 5f) velocityY = 4f;
            if (velocityY < -5f) velocityY = -4f;
            return;
        }

        float acceleration = VanillaFishNpcCatalog1458.UsesLargePursuit(type) ? 0.15f : 0.1f;
        float maximumX = VanillaFishNpcCatalog1458.UsesLargePursuit(type) ? 5f : 3f;
        float maximumY = VanillaFishNpcCatalog1458.UsesLargePursuit(type) ? 3f : 2f;
        velocityX = Math.Clamp(velocityX + directionX * acceleration, -maximumX, maximumX);
        velocityY = Math.Clamp(velocityY + directionY * acceleration, -maximumY, maximumY);
    }

    private static void ApplyArapaimaIdle(
        ref float velocityX,
        ref float velocityY,
        int directionX,
        int directionY,
        ref float ai0)
    {
        velocityX += directionX * 0.2f;
        if (velocityX is < -2f or > 2f)
            velocityX *= 0.95f;

        if (ai0 == -1f)
        {
            float minimum = directionY < 0 ? -1f : directionY > 0 ? -0.2f : -0.6f;
            velocityY -= 0.02f;
            if (velocityY < minimum)
                ai0 = 1f;
        }
        else
        {
            float maximum = directionY < 0 ? 0.2f : directionY > 0 ? 1f : 0.6f;
            velocityY += 0.02f;
            if (velocityY > maximum)
                ai0 = -1f;
        }
    }

    private static void ApplyOrdinaryIdle(
        NpcTypeId type,
        ref float velocityX,
        ref float velocityY,
        int directionX,
        ref float ai0)
    {
        velocityX += directionX * 0.1f;
        float horizontalThreshold = type == VanillaNpcIds.Dolphin ? 3f : 1f;
        if (velocityX < -horizontalThreshold || velocityX > horizontalThreshold)
            velocityX *= 0.95f;

        if (ai0 == -1f)
        {
            velocityY -= 0.01f;
            if (velocityY < -0.3f)
                ai0 = 1f;
        }
        else
        {
            velocityY += 0.01f;
            if (velocityY > 0.3f)
                ai0 = -1f;
        }
    }

    private static void ApplyClosest(
        VanillaBlueSlimeTargetRefresh closest,
        bool faceTarget,
        ref ushort target,
        ref int directionX,
        ref int directionY)
    {
        if (!closest.HasTarget)
            return;

        target = closest.Target;
        if (!faceTarget)
            return;
        directionX = closest.DirectionX;
        directionY = closest.DirectionY;
    }
}
